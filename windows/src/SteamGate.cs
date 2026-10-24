using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Interop.UIAutomationClient;

namespace ClaudeAutoPass
{
    /// <summary>
    /// Presses "查看物品 / View Item" on Steam Workshop's adult-content gate, in the Steam client or a browser, through
    /// UI Automation (no mouse movement). Steam draws these buttons as links or plain clickable boxes, so any element with
    /// the exact label counts, and the press falls back from the element to its clickable parent. Must run on an MTA thread.
    /// </summary>
    sealed class SteamGateScanner
    {
        const int NameProperty = 30005;
        const int ControlTypeProperty = 30003;
        const int InvokePattern = 10000;
        const int LegacyIAccessiblePattern = 10018;
        const int MaxGateDistance = 8;
        const int MaxTexts = 400;

        readonly IUIAutomation uia;
        readonly IUIAutomationCondition viewCondition, cancelCondition;
        readonly IUIAutomationCacheRequest cache;
        readonly IUIAutomationTreeWalker walker;
        readonly Func<string, bool> isTarget;
        readonly Func<string, bool> titleFilter;

        /// <param name="isTargetProcess">Process filter; Steam and common browsers by default, Edge in CI.</param>
        public SteamGateScanner(Func<string, bool> isTargetProcess = null)
        {
            isTarget = isTargetProcess ?? SteamRules.IsTargetProcess;
            uia = Scanner.CreateAutomation();
            viewCondition = AnyName(SteamRules.ViewLabels);
            cancelCondition = AnyName(SteamRules.CancelLabels);
            cache = uia.CreateCacheRequest();
            cache.AddProperty(NameProperty);
            cache.AddProperty(ControlTypeProperty);
            walker = uia.ControlViewWalker;
            titleFilter = SteamRules.IsSteamPageTitle;
        }

        IUIAutomationCondition AnyName(IEnumerable<string> names)
        {
            IUIAutomationCondition result = null;
            foreach (string name in names)
            {
                IUIAutomationCondition c = uia.CreatePropertyCondition(NameProperty, name);
                result = result == null ? c : uia.CreateOrCondition(result, c);
            }
            return result;
        }

        /// <summary>Steam client windows, and browser windows whose open tab is a Steam page; minimized ones are skipped.</summary>
        public List<Native.Window> TargetWindows()
        {
            var steam = new HashSet<int>();
            var browsers = new HashSet<int>();
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    if (!isTarget(p.ProcessName)) continue;
                    if (SteamRules.IsBrowserProcess(p.ProcessName)) browsers.Add(p.Id); else steam.Add(p.Id);
                }
                catch (InvalidOperationException) { }
                finally { p.Dispose(); }
            }
            if (steam.Count == 0 && browsers.Count == 0) return new List<Native.Window>();
            var all = new HashSet<int>(steam);
            all.UnionWith(browsers);
            return Native.TopLevelWindows(all)
                .Where(w => w.Visible && !Native.IsIconic(w.Handle))
                .Where(w => steam.Contains(w.ProcessId) || titleFilter(w.Title))
                .ToList();
        }

        /// <summary>Scans once; returns the pressed label, or null. Notes say why a gate was seen but not pressed.</summary>
        public string ScanOnce(List<string> notes, bool act = true)
        {
            foreach (Native.Window window in TargetWindows())
            {
                IUIAutomationElement root;
                try { root = uia.ElementFromHandle(window.Handle); }
                catch (COMException) { continue; }
                if (root == null) continue;
                IUIAutomationElementArray found;
                try { found = root.FindAllBuildCache(TreeScope.TreeScope_Descendants, viewCondition, cache); }
                catch (COMException) { continue; }
                if (found == null) continue;
                for (int i = 0; i < found.Length; i++)
                {
                    IUIAutomationElement view = found.GetElement(i);
                    string label = Cached(view);
                    if (!SteamRules.IsViewLabel(label)) continue;
                    if (FindGate(view, root) == null)
                    {
                        notes?.Add($"Steam 未点：不像年龄提示 / not a gate \"{label}\" \"{window.Title}\"");
                        continue;
                    }
                    if (!act) return label;
                    string how = Press(view);
                    if (how.StartsWith("ok", StringComparison.Ordinal)) return label;
                    notes?.Add($"Steam 未点：按下失败 / press failed \"{label}\" {how}");
                }
            }
            return null;
        }

        /// <summary>The nearest ancestor (below the window) that holds the cancel button and the 18+ text, or null.</summary>
        IUIAutomationElement FindGate(IUIAutomationElement view, IUIAutomationElement windowRoot)
        {
            IUIAutomationElement current = view;
            for (int distance = 0; distance < MaxGateDistance; distance++)
            {
                try { current = walker.GetParentElement(current); }
                catch (COMException) { return null; }
                if (current == null || uia.CompareElements(current, windowRoot) != 0) return null;
                IUIAutomationElement cancel;
                try { cancel = current.FindFirst(TreeScope.TreeScope_Descendants, cancelCondition); }
                catch (COMException) { return null; }
                if (cancel == null) continue;
                if (SteamRules.IsGate(Texts(current))) return current;
            }
            return null;
        }

        /// <summary>Names of the box's elements (button labels and the text above them).</summary>
        List<string> Texts(IUIAutomationElement scope)
        {
            var texts = new List<string>();
            try
            {
                IUIAutomationElementArray all = scope.FindAllBuildCache(TreeScope.TreeScope_Descendants, uia.CreateTrueCondition(), cache);
                int n = Math.Min(all?.Length ?? 0, MaxTexts);
                for (int i = 0; i < n; i++)
                {
                    string name = Cached(all.GetElement(i));
                    if (name.Length > 0) texts.Add(name);
                }
            }
            catch (COMException) { }
            return texts;
        }

        /// <summary>
        /// Invokes the element; if it is only the label inside a clickable box (Steam's div buttons), tries the box above it.
        /// "ok ..." when pressed, otherwise why not.
        /// </summary>
        string Press(IUIAutomationElement element)
        {
            var tried = new List<string>();
            IUIAutomationElement current = element;
            for (int level = 0; level < 3 && current != null; level++)
            {
                try
                {
                    if (current.GetCurrentPattern(InvokePattern) is IUIAutomationInvokePattern invoke)
                    {
                        invoke.Invoke();
                        return "ok invoke@" + level;
                    }
                    if (current.GetCurrentPattern(LegacyIAccessiblePattern) is IUIAutomationLegacyIAccessiblePattern legacy
                        && !string.IsNullOrEmpty(legacy.CurrentDefaultAction))
                    {
                        legacy.DoDefaultAction();
                        return "ok legacy@" + level + " " + legacy.CurrentDefaultAction;
                    }
                    tried.Add("L" + level + ":none");
                }
                catch (COMException ex) { tried.Add("L" + level + ":0x" + ex.HResult.ToString("X8")); }
                try { current = walker.GetParentElement(current); }
                catch (COMException) { break; }
            }
            return string.Join(" ", tried);
        }

        static string Cached(IUIAutomationElement e)
        {
            try { return e.CachedName ?? ""; } catch (COMException) { return ""; }
        }

        /// <summary>Steam windows and the gate buttons seen in them, for the diagnostics export.</summary>
        public string Diagnose()
        {
            var sb = new StringBuilder();
            List<Native.Window> windows = TargetWindows();
            sb.AppendLine("Steam / 浏览器中的 Steam 页面窗口 / windows: " + windows.Count);
            foreach (Native.Window w in windows)
            {
                IUIAutomationElement root;
                try { root = uia.ElementFromHandle(w.Handle); }
                catch (COMException ex) { sb.AppendLine($"== 0x{w.Handle.ToInt64():X} \"{w.Title}\" 无法读取 / cannot read: {ex.Message}"); continue; }
                int total = 0;
                try { total = root.FindAll(TreeScope.TreeScope_Descendants, uia.CreateTrueCondition())?.Length ?? 0; }
                catch (COMException) { }
                sb.AppendLine($"== 0x{w.Handle.ToInt64():X} \"{w.Title}\" class={w.ClassName} 元素 / elements: {total}");
                IUIAutomationElementArray found = null;
                try { found = root.FindAllBuildCache(TreeScope.TreeScope_Descendants, viewCondition, cache); }
                catch (COMException) { }
                for (int i = 0; i < (found?.Length ?? 0); i++)
                {
                    IUIAutomationElement e = found.GetElement(i);
                    int type = 0;
                    try { type = e.CachedControlType; } catch (COMException) { }
                    sb.AppendLine($"  - \"{Cached(e)}\" type={type} {(FindGate(e, root) != null ? "[年龄提示 / gate]" : "[不是年龄提示 / not a gate]")}");
                }
            }
            return sb.ToString();
        }
    }
}
