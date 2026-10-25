using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ClaudeAutoPass
{
    /// <summary>
    /// "单击任务栏打开 Claude": Claude Desktop on Windows answers a click on its taskbar button with its quick entry bar rather than
    /// the main window (anthropics/claude-code#22420), so a minimized or closed-to-tray Claude doesn't open on a click, only from
    /// the right-click menu. When one of Claude's windows is activated or shown, or Claude is launched again (the pinned taskbar
    /// icon), while its main window is minimized or hidden, this shows the main window and puts it in front. Claude's window
    /// events are also written to the scan log while it is closed, to see what a click really does.
    /// </summary>
    static class ClaudeTaskbar
    {
        static readonly WinEventProc Callback = OnEvent;
        static readonly List<IntPtr> Hooks = new List<IntPtr>();
        static readonly Dictionary<int, bool> ClaudePids = new Dictionary<int, bool>();
        static Func<int, bool> isClaudePid;
        static Func<bool> enabled;
        static Action<string> log;
        static bool chromiumOnly;
        static DateTime lastRestore, lastMinimize, lastHide, pendingAt = DateTime.MinValue;
        static System.Windows.Forms.Timer decideTimer, launchTimer;
        static HashSet<int> knownMainPids;
        static string pendingReason;

        const double GuardSeconds = 0.6;

        public static bool Running => Hooks.Count > 0;

        /// <summary>Call on a thread with a message loop.</summary>
        public static void Start(Func<bool> isEnabled, Action<string> logNote, Func<int, bool> claudePid = null, bool chromiumWindowsOnly = true)
        {
            enabled = isEnabled;
            log = logNote;
            isClaudePid = claudePid ?? IsClaudeProcess;
            chromiumOnly = chromiumWindowsOnly;
            decideTimer = new System.Windows.Forms.Timer { Interval = 150 }; // short, so the quick entry barely shows
            decideTimer.Tick += (s, e) => Decide();
            // Launching Claude again (its pinned taskbar icon when no window is shown) may show nothing at all: watch for it.
            if (claudePid == null)
            {
                launchTimer = new System.Windows.Forms.Timer { Interval = 400 };
                launchTimer.Tick += (s, e) => WatchLaunches();
                launchTimer.Start();
            }
            // Activation and minimizing are rare, so those are watched everywhere. Windows being shown or hidden is very common
            // (every app, all the time), so that is watched only in Claude's own processes: a system-wide hook for it made
            // every program report each such change to this tool.
            foreach (uint[] range in new[] { new[] { EventForeground, EventForeground }, new[] { EventMinimizeStart, EventMinimizeEnd } })
            {
                IntPtr hook = SetWinEventHook(range[0], range[1], IntPtr.Zero, Callback, 0, 0, OutOfContext);
                if (hook != IntPtr.Zero) Hooks.Add(hook);
            }
            hookTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            hookTimer.Tick += (s, e) => RefreshShowHooks();
            hookTimer.Start();
            RefreshShowHooks();
        }

        static readonly Dictionary<int, IntPtr> ShowHooks = new Dictionary<int, IntPtr>();
        static System.Windows.Forms.Timer hookTimer;

        /// <summary>Keeps one show/hide hook per Claude process that has titled top-level windows.</summary>
        static void RefreshShowHooks()
        {
            try
            {
                var owners = new HashSet<int>();
                EnumWindows((h, _) =>
                {
                    GetWindowThreadProcessId(h, out uint pid);
                    if (GetWindowTextLength(h) > 0 && IsClaude((int)pid)) owners.Add((int)pid);
                    return true;
                }, IntPtr.Zero);
                foreach (int pid in new List<int>(ShowHooks.Keys))
                    if (!owners.Contains(pid)) { UnhookWinEvent(ShowHooks[pid]); ShowHooks.Remove(pid); }
                foreach (int pid in owners)
                {
                    if (ShowHooks.ContainsKey(pid)) continue;
                    IntPtr hook = SetWinEventHook(EventShow, EventHide, IntPtr.Zero, Callback, (uint)pid, 0, OutOfContext);
                    if (hook != IntPtr.Zero) ShowHooks[pid] = hook;
                }
            }
            catch (Exception ex) { log?.Invoke("单击任务栏打开 Claude 出错 / hook error: " + ex.Message); }
        }

        public static void Stop()
        {
            foreach (IntPtr hook in Hooks) UnhookWinEvent(hook);
            Hooks.Clear();
            foreach (IntPtr hook in ShowHooks.Values) UnhookWinEvent(hook);
            ShowHooks.Clear();
            hookTimer?.Dispose();
            hookTimer = null;
            decideTimer?.Dispose();
            decideTimer = null;
            launchTimer?.Dispose();
            launchTimer = null;
            trayCheck?.Dispose();
            trayCheck = null;
        }

        static bool IsClaudeProcess(int pid)
        {
            try { using (Process p = Process.GetProcessById(pid)) return ApprovalRules.IsClaudeProcess(p.ProcessName); }
            catch (Exception) { return false; }
        }

        static bool IsClaude(int pid)
        {
            if (ClaudePids.TryGetValue(pid, out bool known)) return known;
            if (ClaudePids.Count > 500) ClaudePids.Clear(); // process ids get reused
            return ClaudePids[pid] = isClaudePid(pid);
        }

        static void OnEvent(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (idObject != 0 || idChild != 0 || hwnd == IntPtr.Zero) return; // whole windows only
            try
            {
                if (GetAncestor(hwnd, RootAncestor) != hwnd) return; // top-level windows only
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (!IsClaude((int)pid)) return;
                bool titled = GetWindowTextLength(hwnd) > 0;
                if (ev == EventHide && !titled) return; // Chromium hides many helper windows
                string name = ev == EventForeground ? "前台 / foreground" : ev == EventShow ? "显示 / shown" : ev == EventHide ? "隐藏 / hidden"
                    : ev == EventMinimizeStart ? "最小化 / minimized" : "还原 / restored";
                IntPtr main = ClosedMainWindow();
                // Only while Claude is closed (or being minimized, hidden or restored), so normal use doesn't fill the log.
                if (main != IntPtr.Zero || ev == EventMinimizeStart || ev == EventMinimizeEnd || ev == EventHide) log?.Invoke($"Claude 窗口{name}: {Describe(hwnd)}");
                if (ev == EventMinimizeStart) { lastMinimize = DateTime.UtcNow; return; }
                if (ev == EventHide) { if (IsMainLike(hwnd)) lastHide = DateTime.UtcNow; return; }
                if (ev == EventMinimizeEnd) return;
                Trigger(main, name);
            }
            catch (Exception ex) { log?.Invoke("单击任务栏打开 Claude 出错 / taskbar error: " + ex.Message); }
        }

        /// <summary>Decides a moment later: minimizing or hiding a window also activates or shows windows, and those events can
        /// arrive before the minimize event itself.</summary>
        static void Trigger(IntPtr main, string reason)
        {
            if (enabled == null || !enabled() || main == IntPtr.Zero) return;
            if (pendingAt == DateTime.MinValue) { pendingAt = DateTime.UtcNow; pendingReason = reason; }
            decideTimer?.Stop();
            decideTimer?.Start();
        }

        static void Decide()
        {
            decideTimer?.Stop();
            DateTime at = pendingAt;
            string reason = pendingReason;
            pendingAt = DateTime.MinValue;
            try
            {
                if (enabled == null || !enabled()) return;
                IntPtr main = ClosedMainWindow();
                if (main == IntPtr.Zero) return;
                // Not when Claude was minimized or closed just before (or after): that event came from minimizing or closing,
                // e.g. a click on the button of the active Claude, which may also open the quick entry. Nor right after a restore.
                // Short: the owner minimizes and clicks again within a second or two.
                if (Math.Abs((at - lastMinimize).TotalSeconds) < GuardSeconds || Math.Abs((at - lastHide).TotalSeconds) < GuardSeconds
                    || (DateTime.UtcNow - lastRestore).TotalSeconds < 1) return;
                lastRestore = DateTime.UtcNow;
                Bring(main);
                log?.Invoke($"单击任务栏（{reason}）：已打开 Claude 主窗口 / opened Claude's main window: {Describe(main)}");
            }
            catch (Exception ex) { log?.Invoke("单击任务栏打开 Claude 出错 / taskbar error: " + ex.Message); }
        }

        /// <summary>A new Claude main process (no --type= switch) while the main window is closed: Claude was launched again.</summary>
        static void WatchLaunches()
        {
            try
            {
                // Only while the main window is closed; start counting afresh each time it closes.
                if (enabled == null || !enabled() || ClosedMainWindow() == IntPtr.Zero) { knownMainPids = null; return; }
                var now = new HashSet<int>();
                foreach (Process p in Process.GetProcesses())
                {
                    try { if (ApprovalRules.IsClaudeProcess(p.ProcessName)) now.Add(p.Id); }
                    catch (InvalidOperationException) { }
                    finally { p.Dispose(); }
                }
                if (knownMainPids == null) { knownMainPids = now; return; }
                var added = new List<int>();
                foreach (int pid in now) if (!knownMainPids.Contains(pid)) added.Add(pid);
                knownMainPids = now;
                if (added.Count == 0 || enabled == null || !enabled()) return;
                IntPtr main = ClosedMainWindow();
                if (main == IntPtr.Zero) return;
                foreach (int pid in added)
                {
                    string commandLine = CommandLine(pid);
                    // A child (renderer, GPU...) has --type=; a second instance exits at once, so it may already be gone (null).
                    if (commandLine != null && commandLine.Contains("--type=")) continue;
                    log?.Invoke($"Claude 又被启动 / launched again: pid {pid} {(commandLine == null ? "(已退出 / exited)" : "")}");
                    Trigger(main, "再次启动 / launched again");
                    break;
                }
            }
            catch (Exception ex) { log?.Invoke("单击任务栏打开 Claude 出错 / launch watch error: " + ex.Message); }
        }

        static string CommandLine(int pid)
        {
            try
            {
                using (var searcher = new System.Management.ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE ProcessId=" + pid))
                    foreach (System.Management.ManagementObject o in searcher.Get())
                        using (o) return o["CommandLine"] as string ?? "";
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>A window that looks like Claude's main window: titled, resizable, not always-on-top (the quick entry is).</summary>
        static bool IsMainLike(IntPtr h)
        {
            if (GetWindowTextLength(h) == 0) return false;
            if (chromiumOnly && !ClassOf(h).StartsWith("Chrome_WidgetWin_1", StringComparison.Ordinal)) return false;
            if ((GetWindowLong(h, Style) & ThickFrame) == 0) return false;
            int ex = GetWindowLong(h, ExStyle);
            return (ex & (Topmost | ToolWindow)) == 0;
        }

        /// <summary>
        /// Claude's main window when it is minimized or hidden (closed to the tray): the largest main-like window. None while any
        /// main-like window is open on screen.
        /// </summary>
        static IntPtr ClosedMainWindow()
        {
            IntPtr best = IntPtr.Zero;
            long bestArea = -1;
            bool open = false;
            EnumWindows((h, _) =>
            {
                GetWindowThreadProcessId(h, out uint pid);
                if (!IsClaude((int)pid) || !IsMainLike(h)) return true;
                if (IsWindowVisible(h) && !IsIconic(h)) { open = true; return false; }
                var p = new WINDOWPLACEMENT { length = Marshal.SizeOf(typeof(WINDOWPLACEMENT)) };
                if (!GetWindowPlacement(h, ref p)) return true;
                long area = (long)(p.rcNormalPosition.Right - p.rcNormalPosition.Left) * (p.rcNormalPosition.Bottom - p.rcNormalPosition.Top);
                if (area > bestArea) { bestArea = area; best = h; }
                return true;
            }, IntPtr.Zero);
            return open ? IntPtr.Zero : best;
        }

        /// <summary>Restores the window and puts it in front (borrowing the input of the window that has the focus now).</summary>
        static void Bring(IntPtr h)
        {
            // A main window Claude itself hid (X, closed to the tray) must be shown by Claude: showing it from outside makes
            // the window appear but Chromium keeps its content hidden, so it looks frozen and takes no input (owner's report
            // on 1.2.3). Claude shows it when its tray icon is clicked, so click that instead.
            if (!IsWindowVisible(h) && UseTrayIcon && ClickTrayIcon(h))
            {
                ScheduleTrayCheck(h);
                return;
            }
            if (!IsWindowVisible(h)) ShowWindow(h, Show);
            if (IsIconic(h)) ShowWindow(h, Restore);
            uint self = GetCurrentThreadId();
            uint focused = GetWindowThreadProcessId(GetForegroundWindow(), out _);
            bool attached = focused != 0 && focused != self && AttachThreadInput(self, focused, true);
            try
            {
                BringWindowToTop(h);
                SetForegroundWindow(h);
            }
            finally { if (attached) AttachThreadInput(self, focused, false); }
        }

        // Electron's tray icons: a hidden "Electron_NotifyIconHostWindow" in the main process receives the icon's mouse
        // messages as WM_APP + 1, with the icon's id (from 2 up) in wParam and the mouse message in lParam.
        const string TrayHostClass = "Electron_NotifyIconHostWindow";
        const uint TrayMessage = 0x8000 + 1, LeftButtonDown = 0x0201;
        const int FirstTrayIconId = 2, LastTrayIconId = 9;

        /// <summary>Clicks the tray icon of the process that owns this window; false when it has no Electron tray icon.</summary>
        static bool ClickTrayIcon(IntPtr main)
        {
            GetWindowThreadProcessId(main, out uint pid);
            var hosts = new List<IntPtr>();
            foreach (IntPtr parent in new[] { IntPtr.Zero, MessageOnly })
            {
                IntPtr host = IntPtr.Zero;
                while ((host = FindWindowEx(parent, host, TrayHostClass, null)) != IntPtr.Zero)
                {
                    GetWindowThreadProcessId(host, out uint owner);
                    if (owner == pid) hosts.Add(host);
                }
            }
            if (hosts.Count == 0) return false;
            AllowSetForegroundWindow(pid); // so Claude may bring its window to the front
            foreach (IntPtr host in hosts)
                for (int id = FirstTrayIconId; id <= LastTrayIconId; id++) // ids that match no icon are ignored
                    PostMessage(host, TrayMessage, (IntPtr)id, (IntPtr)LeftButtonDown);
            log?.Invoke("单击任务栏：已单击 Claude 的托盘图标来打开主窗口 / clicked Claude's tray icon to open its main window");
            return true;
        }

        /// <summary>False only in a self-test comparing with showing the window from outside.</summary>
        internal static bool UseTrayIcon = true;

        static System.Windows.Forms.Timer trayCheck;

        /// <summary>Notes in the scan log whether the tray click opened the main window.</summary>
        static void ScheduleTrayCheck(IntPtr main)
        {
            trayCheck?.Dispose();
            var check = trayCheck = new System.Windows.Forms.Timer { Interval = 1500 };
            check.Tick += (s, e) =>
            {
                check.Stop();
                if (!IsWindowVisible(main))
                    log?.Invoke("单击任务栏：托盘图标没打开 Claude 主窗口，请右键托盘图标打开 / the tray click did not open Claude's main window");
            };
            check.Start();
        }

        static readonly IntPtr MessageOnly = new IntPtr(-3);

        static string ClassOf(IntPtr h)
        {
            var cls = new StringBuilder(256);
            GetClassName(h, cls, cls.Capacity);
            return cls.ToString();
        }

        public static string Describe(IntPtr h)
        {
            var title = new StringBuilder(256);
            GetWindowText(h, title, title.Capacity);
            GetWindowRect(h, out RECT r);
            int ex = GetWindowLong(h, ExStyle);
            IntPtr owner = GetWindow(h, Owner);
            return $"0x{h.ToInt64():X} \"{title}\" class={ClassOf(h)} visible={IsWindowVisible(h)} minimized={IsIconic(h)} "
                 + $"rect={r.Left},{r.Top},{r.Right - r.Left}x{r.Bottom - r.Top}{((ex & ToolWindow) != 0 ? " tool" : "")}{((ex & AppWindow) != 0 ? " app" : "")}"
                 + (owner != IntPtr.Zero ? $" owner=0x{owner.ToInt64():X}" : "");
        }

        /// <summary>For the diagnostics: every top-level window of the given processes.</summary>
        public static string DescribeAll(ISet<int> processIds)
        {
            var sb = new StringBuilder();
            foreach (Native.Window w in Native.TopLevelWindows(processIds))
            {
                // Chromium keeps many invisible helper windows; list only those that can matter for the taskbar.
                if (!w.Visible && w.Title.Length == 0) continue;
                sb.AppendLine("  " + Describe(w.Handle));
            }
            return sb.ToString();
        }

        const uint EventForeground = 0x0003, EventMinimizeStart = 0x0016, EventMinimizeEnd = 0x0017, EventShow = 0x8002, EventHide = 0x8003, OutOfContext = 0;
        const uint RootAncestor = 2, Owner = 4;
        const int Show = 5, Restore = 9, Style = -16, ExStyle = -20, ThickFrame = 0x40000, Topmost = 0x8, ToolWindow = 0x80, AppWindow = 0x40000;

        delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
        delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        struct WINDOWPLACEMENT
        {
            public int length, flags, showCmd;
            public POINT ptMinPosition, ptMaxPosition;
            public RECT rcNormalPosition;
        }

        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint process, uint thread, uint flags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
        [DllImport("user32.dll")] static extern bool GetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT placement);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool AttachThreadInput(uint attach, uint to, bool on);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(uint pid);
    }
}
