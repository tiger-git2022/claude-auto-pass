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

        static IUIAutomation CreateAutomation()
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
                IUIAutomationElement root;
                try { root = uia.ElementFromHandle(window.Handle); }
                catch (COMException) { continue; }
                if (root == null) continue;
                List<Candidate> found = Buttons(root);
                result.Buttons += found.Count;
                foreach (Candidate c in found)
                {
                    if (c.Kind != Kind.Session && c.Kind != Kind.Once) continue;
                    if (best != null && (best.Kind == Kind.Session || c.Kind != Kind.Session)) continue;
                    if (!CachedEnabled(c.Element) || !ids.Contains(CachedProcessId(c.Element))) continue;
                    if (InApprovalGroup(c.Element, root)) best = c;
                }
                if (best != null && best.Kind == Kind.Session) break;
            }
            if (best != null && act && Press(best.Element))
            {
                result.Clicked = best.Kind;
                result.Label = best.Label;
            }
            return result;
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

        static bool Press(IUIAutomationElement element)
        {
            try
            {
                if (element.GetCurrentPattern(InvokePattern) is IUIAutomationInvokePattern invoke)
                {
                    invoke.Invoke();
                    return true;
                }
                if (element.GetCurrentPattern(LegacyIAccessiblePattern) is IUIAutomationLegacyIAccessiblePattern legacy)
                {
                    legacy.DoDefaultAction();
                    return true;
                }
            }
            catch (COMException) { }
            return false;
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
                sb.AppendLine($"== 窗口 / Window 0x{w.Handle.ToInt64():X} \"{w.Title}\" class={w.ClassName} visible={w.Visible}");
                IUIAutomationElement root;
                try { root = uia.ElementFromHandle(w.Handle); }
                catch (COMException ex) { sb.AppendLine("  无法读取 / cannot read: " + ex.Message); continue; }
                int total = 0;
                try { total = root.FindAll(TreeScope.TreeScope_Descendants, uia.CreateTrueCondition())?.Length ?? 0; }
                catch (COMException) { }
                List<Candidate> found = Buttons(root);
                sb.AppendLine($"  元素总数 / elements: {total}，按钮 / buttons: {found.Count}");
                if (total < 30)
                    sb.AppendLine("  元素很少：Claude 可能没有开启辅助功能树。可在托盘菜单选择「以辅助功能模式重启 Claude」。 / Few elements: try 'Restart Claude with accessibility'.");
                foreach (Candidate c in found)
                {
                    string group = c.Kind == Kind.Session || c.Kind == Kind.Once ? (InApprovalGroup(c.Element, root) ? " [审批组 / group]" : " [单独 / isolated]") : "";
                    sb.AppendLine($"  - \"{c.Label}\" → {c.Kind}{group}{(CachedEnabled(c.Element) ? "" : " (disabled)")}");
                }
            }
            return sb.ToString();
        }
    }
}
