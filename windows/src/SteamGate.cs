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
    /// the exact label counts, the gate is recognised by layout ("取消" beside it, the 18+ text above), and the press falls
    /// back from the label to its clickable parent. Must run on an MTA thread.
    /// </summary>
    sealed class SteamGateScanner
    {
        const int BoundingRectangleProperty = 30001;
        const int NameProperty = 30005;
        const int TextControlType = 50020;
        const int DocumentControlType = 50030;
        const int ControlTypeProperty = 30003;
        const int InvokePattern = 10000;
        const int LegacyIAccessiblePattern = 10018;
        const int MaxTexts = 5000;

        readonly IUIAutomation uia;
        readonly IUIAutomationCondition viewCondition, cancelCondition, textCondition, cancelOrTextCondition, documentCondition;
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
            cache.AddProperty(BoundingRectangleProperty);
            textCondition = uia.CreatePropertyCondition(ControlTypeProperty, TextControlType);
            cancelOrTextCondition = uia.CreateOrCondition(cancelCondition, textCondition);
            documentCondition = uia.CreatePropertyCondition(ControlTypeProperty, DocumentControlType);
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
                string label = ScanWindow(window.Handle, window.Title, notes, act);
                if (label != null) return label;
            }
            return null;
        }

        sealed class Page
        {
            public string Signature;
            public DateTime ChangedAt, ScannedAt, DocumentTriedAt;
            public IUIAutomationElement Document;
        }

        readonly Dictionary<IntPtr, Page> pages = new Dictionary<IntPtr, Page>();
        readonly Dictionary<int, string> processNames = new Dictionary<int, string>();

        /// <summary>Scan in full this long after the page changed, then only now and then (each scan makes the page work).</summary>
        const double BurstSeconds = 8, IdleScanSeconds = 2.5;

        /// <summary>
        /// For the tray app: scans only the window in front (the gate appears when the owner opens an item), in full right after
        /// its page changed (title or page name) and only every few seconds otherwise, so Steam and the browser are not kept
        /// busy answering UI Automation all the time. Returns the pressed label, or null; scanned says whether it scanned.
        /// </summary>
        public string ScanIfDue(List<string> notes, out bool scanned)
        {
            scanned = false;
            IntPtr front = GetForegroundWindow();
            if (front == IntPtr.Zero || Native.IsIconic(front)) return null;
            GetWindowThreadProcessId(front, out uint pid);
            string process = ProcessName((int)pid);
            if (process == null || !isTarget(process)) return null;
            var title = new StringBuilder(512);
            GetWindowText(front, title, title.Capacity);
            if (SteamRules.IsBrowserProcess(process) && !titleFilter(title.ToString())) return null;

            if (!pages.TryGetValue(front, out Page page))
            {
                if (pages.Count > 50) pages.Clear();
                pages[front] = page = new Page { ChangedAt = DateTime.UtcNow };
            }
            DateTime now = DateTime.UtcNow;
            string signature = title + "|" + DocumentName(front, page, now);
            if (signature != page.Signature) { page.Signature = signature; page.ChangedAt = now; }
            if ((now - page.ChangedAt).TotalSeconds >= BurstSeconds && (now - page.ScannedAt).TotalSeconds < IdleScanSeconds) return null;
            page.ScannedAt = now;
            scanned = true;
            return ScanWindow(front, title.ToString(), notes, true);
        }

        string ProcessName(int pid)
        {
            if (processNames.TryGetValue(pid, out string name)) return name;
            if (processNames.Count > 500) processNames.Clear(); // process ids get reused
            try { using (Process p = Process.GetProcessById(pid)) name = p.ProcessName; }
            catch (Exception) { name = null; }
            return processNames[pid] = name;
        }

        /// <summary>The page's name (its title), read from the page element found once per window: one cheap call per tick.</summary>
        string DocumentName(IntPtr window, Page page, DateTime now)
        {
            if (page.Document != null)
            {
                try { return page.Document.CurrentName ?? ""; }
                catch (COMException) { page.Document = null; } // navigated away: that page is gone
            }
            if ((now - page.DocumentTriedAt).TotalSeconds < 2) return "";
            page.DocumentTriedAt = now;
            try
            {
                IUIAutomationElement root = uia.ElementFromHandle(window);
                page.Document = root?.FindFirst(TreeScope.TreeScope_Descendants, documentCondition);
                return page.Document?.CurrentName ?? "";
            }
            catch (COMException) { return ""; }
        }

        string ScanWindow(IntPtr handle, string title, List<string> notes, bool act)
        {
            IUIAutomationElement root;
            try { root = uia.ElementFromHandle(handle); }
            catch (COMException) { return null; }
            if (root == null) return null;
            List<IUIAutomationElement> views = Find(root, viewCondition);
            foreach (IUIAutomationElement view in views)
            {
                string label = Cached(view);
                if (!SteamRules.IsViewLabel(label)) continue;
                string why = WhyNotGate(view, root);
                if (why != null)
                {
                    notes?.Add($"Steam 未点：不像年龄提示 / not a gate \"{label}\" {why} \"{title}\"");
                    continue;
                }
                if (!act) return label;
                string how = Press(view);
                if (how.StartsWith("ok", StringComparison.Ordinal)) return label;
                notes?.Add($"Steam 未点：按下失败 / press failed \"{label}\" {how}");
            }
            return null;
        }

        /// <summary>
        /// Looks for "取消" and the 18+ text in ever larger boxes around the button (its parents) instead of the whole page:
        /// the gate is a small dialog, and searching the whole page twice more made each press take over a second.
        /// </summary>
        string WhyNotGate(IUIAutomationElement view, IUIAutomationElement root)
        {
            string why = "隐藏 / hidden";
            IUIAutomationElement current = view;
            for (int level = 0; level < MaxGateLevels; level++)
            {
                try { current = walker.GetParentElement(current); }
                catch (COMException) { break; }
                if (current == null) break;
                List<IUIAutomationElement> found = Find(current, cancelOrTextCondition);
                why = WhyNotGate(view, found.Where(e => SteamRules.IsCancelLabel(Cached(e))).ToList(), found);
                if (why == null) return null;
                if (uia.CompareElements(current, root) != 0) break;
            }
            return why;
        }

        const int MaxGateLevels = 8;

        List<IUIAutomationElement> Find(IUIAutomationElement root, IUIAutomationCondition condition)
        {
            var list = new List<IUIAutomationElement>();
            try
            {
                IUIAutomationElementArray array = root.FindAllBuildCache(TreeScope.TreeScope_Descendants, condition, cache);
                int n = Math.Min(array?.Length ?? 0, MaxTexts);
                for (int i = 0; i < n; i++) list.Add(array.GetElement(i));
            }
            catch (COMException) { }
            return list;
        }

        /// <summary>Null when "取消" is right next to the button and the 18+ text right above them; otherwise why not.</summary>
        static string WhyNotGate(IUIAutomationElement view, List<IUIAutomationElement> cancels, List<IUIAutomationElement> texts)
        {
            Box at = Rect(view);
            if (at.IsEmpty) return "隐藏 / hidden";
            IUIAutomationElement cancel = cancels.FirstOrDefault(c => SteamRules.IsCancelLabel(Cached(c)) && SteamRules.IsButtonPair(at, Rect(c)));
            if (cancel == null) return $"旁边没有取消 / no cancel beside {at}";
            Box buttons = SteamRules.Union(at, Rect(cancel));
            if (!texts.Any(t => SteamRules.IsGateText(Cached(t)) && SteamRules.IsAbove(buttons, Rect(t))))
                return $"上方没有年龄提示 / no age text above {buttons}";
            return null;
        }

        static Box Rect(IUIAutomationElement e)
        {
            try
            {
                tagRECT r = e.CachedBoundingRectangle;
                return new Box(r.left, r.top, r.right, r.bottom);
            }
            catch (COMException) { return default(Box); }
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

        static int Type(IUIAutomationElement e)
        {
            try { return e.CachedControlType; } catch (COMException) { return 0; }
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
                List<IUIAutomationElement> views = Find(root, viewCondition);
                List<IUIAutomationElement> cancels = Find(root, cancelCondition);
                List<IUIAutomationElement> texts = views.Count > 0 ? Find(root, textCondition) : new List<IUIAutomationElement>();
                foreach (IUIAutomationElement e in views.Concat(cancels))
                    sb.AppendLine($"  - \"{Cached(e)}\" type={Type(e)} {Rect(e)}"
                        + (SteamRules.IsViewLabel(Cached(e)) ? " " + (WhyNotGate(e, cancels, texts) ?? "[年龄提示 / gate]") : ""));
                foreach (IUIAutomationElement t in texts.Where(t => SteamRules.IsGateText(Cached(t))))
                    sb.AppendLine($"  · 提示文字 / text \"{Cached(t)}\" {Rect(t)}");
                foreach (IUIAutomationElement e in views)
                {
                    sb.AppendLine($"  上层 / parents of \"{Cached(e)}\" {Rect(e)}:");
                    IUIAutomationElement current = e;
                    for (int level = 0; level < 5 && current != null; level++)
                    {
                        string action = "";
                        try
                        {
                            if ((object)current.GetCurrentPattern(InvokePattern) != null) action += " invoke";
                            if (current.GetCurrentPattern(LegacyIAccessiblePattern) is IUIAutomationLegacyIAccessiblePattern legacy) action += " default=\"" + legacy.CurrentDefaultAction + "\"";
                            string name = current.CurrentName ?? "";
                            sb.AppendLine($"    {level}: type={current.CurrentControlType} name=\"{(name.Length > 40 ? name.Substring(0, 40) + "…" : name)}\"{action}");
                            current = walker.GetParentElement(current);
                        }
                        catch (COMException) { break; }
                        if (current != null && uia.CompareElements(current, root) != 0) break;
                    }
                }
            }
            return sb.ToString();
        }

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
    }
}
