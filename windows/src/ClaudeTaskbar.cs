using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ClaudeAutoPass
{
    /// <summary>
    /// "单击任务栏打开 Claude": Claude Desktop on Windows answers a click on its taskbar button with its quick entry bar rather than
    /// the main window (anthropics/claude-code#22420), so a minimized Claude doesn't open on a click, only from the right-click
    /// menu. When one of Claude's windows is activated or shown while its main window is minimized, this restores the main window
    /// and puts it in front. Claude's window events are also written to the scan log, to see what a click really does.
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
        static DateTime lastRestore, lastMinimize, pendingAt = DateTime.MinValue;
        static System.Windows.Forms.Timer decideTimer;

        public static bool Running => Hooks.Count > 0;

        /// <summary>Call on a thread with a message loop.</summary>
        public static void Start(Func<bool> isEnabled, Action<string> logNote, Func<int, bool> claudePid = null, bool chromiumWindowsOnly = true)
        {
            enabled = isEnabled;
            log = logNote;
            isClaudePid = claudePid ?? IsClaudeProcess;
            chromiumOnly = chromiumWindowsOnly;
            decideTimer = new System.Windows.Forms.Timer { Interval = 400 };
            decideTimer.Tick += (s, e) => Decide();
            foreach (uint[] range in new[] { new[] { EventForeground, EventForeground }, new[] { EventMinimizeStart, EventMinimizeEnd }, new[] { EventShow, EventShow } })
            {
                IntPtr hook = SetWinEventHook(range[0], range[1], IntPtr.Zero, Callback, 0, 0, OutOfContext);
                if (hook != IntPtr.Zero) Hooks.Add(hook);
            }
        }

        public static void Stop()
        {
            foreach (IntPtr hook in Hooks) UnhookWinEvent(hook);
            Hooks.Clear();
            decideTimer?.Dispose();
            decideTimer = null;
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
                string name = ev == EventForeground ? "前台 / foreground" : ev == EventShow ? "显示 / shown" : ev == EventMinimizeStart ? "最小化 / minimized" : "还原 / restored";
                IntPtr main = MinimizedMainWindow();
                // Only while Claude is minimized (or being minimized / restored), so normal use doesn't fill the log.
                if (main != IntPtr.Zero || (ev != EventForeground && ev != EventShow)) log?.Invoke($"Claude 窗口{name}: {Describe(hwnd)}");
                if (ev == EventMinimizeStart) { lastMinimize = DateTime.UtcNow; return; }
                if (ev == EventMinimizeEnd) return;
                if (enabled == null || !enabled() || main == IntPtr.Zero) return;
                // Decide a moment later: minimizing a window also activates or shows it, and those events can arrive before
                // the minimize event itself.
                if (pendingAt == DateTime.MinValue) pendingAt = DateTime.UtcNow;
                decideTimer?.Stop();
                decideTimer?.Start();
            }
            catch (Exception ex) { log?.Invoke("单击任务栏打开 Claude 出错 / taskbar error: " + ex.Message); }
        }

        static void Decide()
        {
            decideTimer?.Stop();
            DateTime at = pendingAt;
            pendingAt = DateTime.MinValue;
            try
            {
                if (enabled == null || !enabled()) return;
                IntPtr main = MinimizedMainWindow();
                if (main == IntPtr.Zero) return;
                // Not when Claude was minimized just before (or after): that event came from minimizing, e.g. a click on the
                // button of the active Claude, which may also open the quick entry. Nor right after a restore.
                if (Math.Abs((at - lastMinimize).TotalSeconds) < 1.5 || (DateTime.UtcNow - lastRestore).TotalSeconds < 1) return;
                lastRestore = DateTime.UtcNow;
                Bring(main);
                log?.Invoke($"单击任务栏：已打开 Claude 主窗口 / opened Claude's main window: {Describe(main)}");
            }
            catch (Exception ex) { log?.Invoke("单击任务栏打开 Claude 出错 / taskbar error: " + ex.Message); }
        }

        /// <summary>Claude's main window when it is minimized: the largest titled (Chromium) window that is an icon.</summary>
        static IntPtr MinimizedMainWindow()
        {
            IntPtr best = IntPtr.Zero;
            long bestArea = -1;
            EnumWindows((h, _) =>
            {
                GetWindowThreadProcessId(h, out uint pid);
                if (!IsWindowVisible(h) || !IsIconic(h) || !IsClaude((int)pid)) return true;
                if (GetWindowTextLength(h) == 0) return true;
                if (chromiumOnly && !ClassOf(h).StartsWith("Chrome_WidgetWin_1", StringComparison.Ordinal)) return true;
                var p = new WINDOWPLACEMENT { length = Marshal.SizeOf(typeof(WINDOWPLACEMENT)) };
                if (!GetWindowPlacement(h, ref p)) return true;
                long area = (long)(p.rcNormalPosition.Right - p.rcNormalPosition.Left) * (p.rcNormalPosition.Bottom - p.rcNormalPosition.Top);
                if (area > bestArea) { bestArea = area; best = h; }
                return true;
            }, IntPtr.Zero);
            return best;
        }

        /// <summary>Restores the window and puts it in front (borrowing the input of the window that has the focus now).</summary>
        static void Bring(IntPtr h)
        {
            ShowWindow(h, Restore);
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

        const uint EventForeground = 0x0003, EventMinimizeStart = 0x0016, EventMinimizeEnd = 0x0017, EventShow = 0x8002, OutOfContext = 0;
        const uint RootAncestor = 2, Owner = 4;
        const int Restore = 9, ExStyle = -20, ToolWindow = 0x80, AppWindow = 0x40000;

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
    }
}
