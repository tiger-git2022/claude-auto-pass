using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Interop.UIAutomationClient;

namespace ClaudeAutoPass
{
    sealed class ScanResult
    {
        public Kind Clicked;
        public string Label;
        public int ClaudeWindows;
        public int Buttons;
        /// <summary>Title of a minimized Claude window whose page can't be read (null when none).</summary>
        public string Blind;
        /// <summary>Why approval-like buttons were not pressed this scan (written to scan-log.txt).</summary>
        public readonly List<string> Notes = new List<string>();
    }

    /// <summary>
    /// Finds Claude Desktop's approval buttons through Windows UI Automation and presses one with the
    /// Invoke pattern (no mouse movement, no coordinates, works while Claude is in the background).
    /// Must be used from an MTA thread.
    /// </summary>
    sealed class Scanner
    {
        // UI Automation ids (UIAutomationClient.h); spelled out because interop constants cannot be embedded.
        const int RuntimeIdProperty = 30000;
        const int ProcessIdProperty = 30002;
        const int ControlTypeProperty = 30003;
        const int NameProperty = 30005;
        const int IsEnabledProperty = 30010;
        const int ButtonControlType = 50000;
        const int InvokePattern = 10000;
        const int LegacyIAccessiblePattern = 10018;
        const int MaxGroupDistance = 5;

        readonly IUIAutomation uia;
        readonly IUIAutomationCondition buttons;
        readonly IUIAutomationCacheRequest cache;
        readonly IUIAutomationTreeWalker walker;
        readonly Func<string, bool> isTarget;

        /// <param name="isTargetProcess">Process-name filter; Claude Desktop by default, a test browser in CI.</param>
        public Scanner(Func<string, bool> isTargetProcess = null)
        {
            isTarget = isTargetProcess ?? ApprovalRules.IsClaudeProcess;
            uia = CreateAutomation();
            buttons = uia.CreatePropertyCondition(ControlTypeProperty, ButtonControlType);
            cache = uia.CreateCacheRequest();
            cache.AddProperty(NameProperty);
            cache.AddProperty(IsEnabledProperty);
            cache.AddProperty(ProcessIdProperty);
            cache.AddProperty(RuntimeIdProperty);
            walker = uia.ControlViewWalker;
        }

        internal static IUIAutomation CreateAutomation()
        {
            // CUIAutomation8 (Windows 8+) first, then the original CUIAutomation.
            foreach (string clsid in new[] { "e22ad333-b25f-460c-83d0-0581107395c9", "ff48dba4-60ef-4201-aa87-54103eef594e" })
            {
                Type type = Type.GetTypeFromCLSID(new Guid(clsid), false);
                if (type == null) continue;
                try { return (IUIAutomation)Activator.CreateInstance(type); }
                catch (COMException) { }
            }
            throw new InvalidOperationException("Windows UI Automation is unavailable");
        }

        /// <summary>Process ids whose name passes the filter.</summary>
        public HashSet<int> TargetProcessIds()
        {
            var ids = new HashSet<int>();
            foreach (Process p in Process.GetProcesses())
            {
                try { if (isTarget(p.ProcessName)) ids.Add(p.Id); }
                catch (InvalidOperationException) { }
                finally { p.Dispose(); }
            }
            return ids;
        }

        /// <summary>Windows worth scanning: visible ones, plus Electron's main window when hidden to the tray.</summary>
        public List<Native.Window> TargetWindows()
        {
            HashSet<int> ids = TargetProcessIds();
            if (ids.Count == 0) return new List<Native.Window>();
            return Native.TopLevelWindows(ids)
                .Where(w => w.Visible || (w.ClassName.StartsWith("Chrome_WidgetWin_1", StringComparison.Ordinal) && w.Title.Length > 0))
                .ToList();
        }

        sealed class Candidate
        {
            public IUIAutomationElement Element;
            public Kind Kind;
            public string Label;
        }

        /// <summary>Scans all target windows once and presses at most one approval button.</summary>
        public ScanResult ScanOnce(bool act = true)
        {
            var result = new ScanResult();
            HashSet<int> ids = TargetProcessIds();
            List<Native.Window> windows = TargetWindows();
            result.ClaudeWindows = windows.Count;
            Candidate best = null;
            foreach (Native.Window window in windows)
            {
                int windowButtons = 0;
                foreach (IUIAutomationElement root in Roots(window))
                {
                List<Candidate> found = Buttons(root);
                windowButtons += found.Count;
                result.Buttons += found.Count;
                foreach (Candidate c in found)
                {
                    if (ApprovalRules.Rank(c.Kind) == 0) continue;
                    if (best != null && ApprovalRules.Rank(c.Kind) <= ApprovalRules.Rank(best.Kind)) continue;
                    if (!CachedEnabled(c.Element)) { result.Notes.Add($"未点：按钮不可用 / disabled \"{c.Label}\""); continue; }
                    if (!ids.Contains(CachedProcessId(c.Element))) { result.Notes.Add($"未点：进程不符 / other process \"{c.Label}\""); continue; }
                    if (InApprovalGroup(c.Element, root)) best = c;
                    else result.Notes.Add($"未点：不像审批卡片 / not a prompt \"{c.Label}\" {AncestorSummary(c.Element, root)}");
                }
                if (best != null && best.Kind == Kind.Session) break;
                }
                // Only the title bar's own buttons: Chromium has not built this page's accessibility tree (seen on minimized
                // Claude windows that were never shown to a UI Automation client).
                if (windowButtons <= 3 && Native.IsIconic(window.Handle)) result.Blind = window.Title;
                if (best != null && best.Kind == Kind.Session) break;
            }
            if (best != null && act)
            {
                string how = Press(best.Element);
                if (how.StartsWith("ok", StringComparison.Ordinal))
                {
                    result.Clicked = best.Kind;
                    result.Label = best.Label;
                }
                else result.Notes.Add($"未点：按下失败 / press failed \"{best.Label}\" {how}");
            }
            return result;
        }

        /// <summary>
        /// UI Automation roots for a window: the window itself, plus its web-content child windows when it is minimized or
        /// hidden (diagnostics show whether they add anything).
        /// </summary>
        IEnumerable<IUIAutomationElement> Roots(Native.Window window)
        {
            var handles = new List<IntPtr> { window.Handle };
            if (!window.Visible || Native.IsIconic(window.Handle))
                handles.AddRange(Native.ContentWindows(window.Handle));
            foreach (IntPtr handle in handles)
            {
                IUIAutomationElement root = null;
                try { root = uia.ElementFromHandle(handle); }
                catch (COMException) { }
                if (root != null) yield return root;
            }
        }

        List<Candidate> Buttons(IUIAutomationElement scope)
        {
            var list = new List<Candidate>();
            IUIAutomationElementArray array;
            try { array = scope.FindAllBuildCache(TreeScope.TreeScope_Descendants, buttons, cache); }
            catch (COMException) { return list; }
            if (array == null) return list;
            for (int i = 0; i < array.Length; i++)
            {
                IUIAutomationElement e = array.GetElement(i);
                string name = CachedName(e);
                list.Add(new Candidate { Element = e, Label = name, Kind = ApprovalRules.Classify(name) });
            }
            return list;
        }

        /// <summary>
        /// Walks up a few levels to find the prompt container, mirroring the Android service. The walk stays
        /// below the window root, so buttons elsewhere in the window or in other apps never count.
        /// </summary>
        bool InApprovalGroup(IUIAutomationElement element, IUIAutomationElement windowRoot)
        {
            IUIAutomationElement current = element;
            for (int distance = 0; distance < MaxGroupDistance; distance++)
            {
                try { current = walker.GetParentElement(current); }
                catch (COMException) { return false; }
                if (current == null || uia.CompareElements(current, windowRoot) != 0) return false;
                List<Kind> kinds = Buttons(current).Select(c => c.Kind).ToList();
                if (ApprovalRules.IsApprovalGroup(kinds)) return true;
            }
            return false;
        }

        /// <summary>"ok ..." when pressed, otherwise why not.</summary>
        static string Press(IUIAutomationElement element)
        {
            try
            {
                if (element.GetCurrentPattern(InvokePattern) is IUIAutomationInvokePattern invoke)
                {
                    invoke.Invoke();
                    return "ok invoke";
                }
                if (element.GetCurrentPattern(LegacyIAccessiblePattern) is IUIAutomationLegacyIAccessiblePattern legacy)
                {
                    legacy.DoDefaultAction();
                    return "ok legacy";
                }
                return "no invoke pattern";
            }
            catch (COMException ex) { return "COM 0x" + ex.HResult.ToString("X8"); }
        }

        /// <summary>Approval-like button kinds at each parent level, e.g. "[LocalProject | LocalProject | Deny,LocalProject]".</summary>
        string AncestorSummary(IUIAutomationElement element, IUIAutomationElement windowRoot)
        {
            var levels = new List<string>();
            IUIAutomationElement current = element;
            for (int distance = 0; distance < MaxGroupDistance + 3; distance++)
            {
                try { current = walker.GetParentElement(current); }
                catch (COMException) { break; }
                if (current == null || uia.CompareElements(current, windowRoot) != 0) { levels.Add("root"); break; }
                List<Candidate> found = Buttons(current);
                levels.Add(found.Count + ":" + string.Join(",", found.Where(b => b.Kind != Kind.None).Select(b => b.Kind.ToString())));
            }
            return "[" + string.Join(" | ", levels) + "]";
        }

        static string CachedName(IUIAutomationElement e)
        {
            try { return e.CachedName ?? ""; } catch (COMException) { return ""; }
        }

        static bool CachedEnabled(IUIAutomationElement e)
        {
            try { return e.CachedIsEnabled != 0; } catch (COMException) { return false; }
        }

        static int CachedProcessId(IUIAutomationElement e)
        {
            try { return e.CachedProcessId; } catch (COMException) { return -1; }
        }

        /// <summary>Lists every button the scanner can see, to tune the rules for new Claude versions.</summary>
        public string Diagnose()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Claude 自动审批诊断 / Claude Auto Pass diagnostics");
            sb.AppendLine("时间 / Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("版本 / Version: " + AppInfo.Version);
            HashSet<int> ids = TargetProcessIds();
            sb.AppendLine("Claude 进程 / processes: " + (ids.Count == 0 ? "未找到 / none" : string.Join(", ", ids)));
            List<Native.Window> all = ids.Count == 0 ? new List<Native.Window>() : Native.TopLevelWindows(ids);
            List<Native.Window> scanned = TargetWindows();
            sb.AppendLine("窗口 / windows: " + all.Count + "，扫描 / scanned: " + scanned.Count);
            foreach (Native.Window w in scanned)
            {
                sb.AppendLine();
                bool minimized = Native.IsIconic(w.Handle);
                sb.AppendLine($"== 窗口 / Window 0x{w.Handle.ToInt64():X} \"{w.Title}\" class={w.ClassName} visible={w.Visible} minimized={minimized}");
                foreach (IntPtr content in Native.ContentWindows(w.Handle))
                    sb.AppendLine($"  网页层 / content window 0x{content.ToInt64():X} visible={Native.IsWindowVisible(content)}");
                List<IUIAutomationElement> roots = Roots(w).ToList();
                if (roots.Count == 0) { sb.AppendLine("  无法读取 / cannot read"); continue; }
                for (int r = 0; r < roots.Count; r++)
                {
                IUIAutomationElement root = roots[r];
                if (r > 0) sb.AppendLine("  -- 隐藏的网页层 / hidden content window");
                int total = 0;
                try { total = root.FindAll(TreeScope.TreeScope_Descendants, uia.CreateTrueCondition())?.Length ?? 0; }
                catch (COMException) { }
                List<Candidate> found = Buttons(root);
                sb.AppendLine($"  元素总数 / elements: {total}，按钮 / buttons: {found.Count}");
                if (total < 30)
                    sb.AppendLine("  元素很少：Claude 可能没有开启辅助功能树。可在托盘菜单选择「以辅助功能模式重启 Claude」。 / Few elements: try 'Restart Claude with accessibility'.");
                foreach (Candidate c in found)
                {
                    string group = ApprovalRules.Rank(c.Kind) > 0 ? (InApprovalGroup(c.Element, root) ? " [审批组 / group]" : " [单独 / isolated]") : "";
                    sb.AppendLine($"  - \"{c.Label}\" → {c.Kind}{group}{(CachedEnabled(c.Element) ? "" : " (disabled)")}");
                    if (ApprovalRules.Rank(c.Kind) > 0 || c.Kind == Kind.Deny) sb.AppendLine("      上层按钮数:审批类 / parents: " + AncestorSummary(c.Element, root));
                }
                }
            }
            return sb.ToString();
        }
    }
}
