using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ClaudeAutoPass
{
    /// <summary>
    /// "把 Claude 藏到屏幕外": instead of minimizing (Chromium then stops updating the page, so new cards are never seen), Claude's
    /// windows are moved past the right edge of the desktop without activating them. With Claude started by "以辅助功能模式重启
    /// Claude", an off-screen window keeps updating (checked with Edge in CI). Clicking Claude in the taskbar (or Alt+Tab)
    /// brings it back where it was.
    /// </summary>
    static class ClaudeHider
    {
        static readonly object Gate = new object();
        static readonly Dictionary<IntPtr, WINDOWPLACEMENT> Hidden = new Dictionary<IntPtr, WINDOWPLACEMENT>();
        static DateTime hiddenAt;

        // Window events (Start): react to a taskbar click at once instead of on the next scan.
        static readonly WinEventProc Callback = OnWinEvent;
        static readonly List<IntPtr> Hooks = new List<IntPtr>();
        static readonly Dictionary<IntPtr, DateTime> Minimizing = new Dictionary<IntPtr, DateTime>();
        static DateTime otherMinimizedAt = DateTime.MinValue;
        static Timer minimizeTimer;
        static Action<string> log;

        /// <summary>Seconds after hiding during which Claude activating itself is not taken as the user switching to it.</summary>
        const double SettleSeconds = 1.5;

        public static bool Any { get { lock (Gate) return Hidden.Count > 0; } }

        /// <summary>
        /// Watches window activation and minimizing (call on a thread with a message loop). Clicking the taskbar button of a hidden
        /// Claude that is still the active window makes Windows minimize it; restoring it then would put it back off-screen,
        /// which looked like the button did nothing. Now that click brings Claude back on-screen.
        /// </summary>
        public static void Start(Action<string> logNote)
        {
            log = logNote;
            minimizeTimer = new Timer { Interval = 250 };
            minimizeTimer.Tick += (s, e) => SettleMinimized();
            foreach (uint ev in new[] { EventForeground, EventMinimizeStart })
            {
                IntPtr hook = SetWinEventHook(ev, ev, IntPtr.Zero, Callback, 0, 0, OutOfContext);
                if (hook != IntPtr.Zero) Hooks.Add(hook);
            }
        }

        public static void Stop()
        {
            foreach (IntPtr hook in Hooks) UnhookWinEvent(hook);
            Hooks.Clear();
            minimizeTimer?.Dispose();
            minimizeTimer = null;
        }

        /// <summary>Moves the given windows off-screen; returns how many were hidden.</summary>
        public static int Hide(IEnumerable<Native.Window> windows)
        {
            int count = 0;
            int left = SystemInformation.VirtualScreen.Right + 2000;
            lock (Gate)
            {
                foreach (Native.Window w in windows)
                {
                    if (!w.Visible || Hidden.ContainsKey(w.Handle)) continue;
                    var original = new WINDOWPLACEMENT { length = Marshal.SizeOf(typeof(WINDOWPLACEMENT)) };
                    if (!GetWindowPlacement(w.Handle, ref original)) continue;
                    RECT r = original.rcNormalPosition;
                    var away = original;
                    away.flags = 0;
                    away.showCmd = ShowNoActivate; // also restores a minimized or maximized window, at the new place
                    away.rcNormalPosition = new RECT { Left = left, Top = r.Top, Right = left + (r.Right - r.Left), Bottom = r.Bottom };
                    if (!SetWindowPlacement(w.Handle, ref away)) continue;
                    // Last in line, so closing or minimizing another program doesn't hand the focus to the hidden Claude (that
                    // would count as switching to it and bring it back).
                    SetWindowPos(w.Handle, Bottom, 0, 0, 0, 0, NoSize | NoMove | NoActivate);
                    Hidden[w.Handle] = original;
                    count++;
                }
                if (count > 0) hiddenAt = DateTime.UtcNow;
            }
            return count;
        }

        /// <summary>Puts every hidden window back where it was (a window that was minimized comes back restored).</summary>
        public static void ShowAll()
        {
            lock (Gate)
            {
                foreach (IntPtr h in new List<IntPtr>(Hidden.Keys)) Restore(h, false);
            }
        }

        /// <summary>
        /// Called by the scan loop: forgets closed windows, and (as a fallback when the window events are not running) brings back a
        /// hidden window the user switched to or minimized.
        /// </summary>
        public static List<string> Tick()
        {
            var notes = new List<string>();
            lock (Gate)
            {
                if (Hidden.Count == 0) return notes;
                IntPtr foreground = GetForegroundWindow();
                bool settled = (DateTime.UtcNow - hiddenAt).TotalSeconds > SettleSeconds;
                foreach (IntPtr h in new List<IntPtr>(Hidden.Keys))
                {
                    // Closed. A window Claude only hid (to its tray) stays tracked, so it still comes back on-screen.
                    if (!IsWindow(h)) { Hidden.Remove(h); continue; }
                    if (h == foreground && settled)
                    {
                        Restore(h, true);
                        notes.Add("切到 Claude，已从屏幕外拿回 / brought Claude back from off-screen");
                    }
                    else if (Hooks.Count == 0 && IsIconic(h))
                    {
                        Restore(h, false, keepMinimized: true);
                        notes.Add("Claude 被最小化，已把位置放回屏幕内 / Claude minimized, its place is back on-screen");
                    }
                }
            }
            return notes;
        }

        static void OnWinEvent(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (idObject != 0 || hwnd == IntPtr.Zero) return; // window events only, not its parts
            string note = null;
            try
            {
                lock (Gate)
                {
                    if (ev == EventMinimizeStart)
                    {
                        if (!Hidden.ContainsKey(hwnd)) { otherMinimizedAt = DateTime.UtcNow; return; }
                        // Decide a moment later, once other windows minimized by the same action (Win+M) have been seen.
                        Minimizing[hwnd] = DateTime.UtcNow;
                        minimizeTimer?.Stop();
                        minimizeTimer?.Start();
                    }
                    else if (ev == EventForeground && Hidden.ContainsKey(hwnd) && (DateTime.UtcNow - hiddenAt).TotalSeconds > SettleSeconds)
                    {
                        Restore(hwnd, true);
                        note = "切到 Claude，已从屏幕外拿回 / brought Claude back from off-screen";
                    }
                }
            }
            catch (Exception ex) { note = "藏到屏幕外出错 / hide error: " + ex.Message; }
            if (note != null) log?.Invoke(note);
        }

        /// <summary>
        /// A hidden Claude was minimized. Alone, that is a click on its taskbar button while it was the active window: bring it
        /// back on-screen. Together with other windows (Win+M, show desktop) it stays minimized, at its old place, so a later
        /// taskbar click opens it where it was.
        /// </summary>
        static void SettleMinimized()
        {
            minimizeTimer?.Stop();
            var notes = new List<string>();
            lock (Gate)
            {
                foreach (KeyValuePair<IntPtr, DateTime> pair in Minimizing)
                {
                    IntPtr h = pair.Key;
                    if (!Hidden.ContainsKey(h)) continue;
                    if (Math.Abs((otherMinimizedAt - pair.Value).TotalMilliseconds) < 500)
                    {
                        Restore(h, false, keepMinimized: true);
                        notes.Add("Claude 和其他窗口一起最小化，已把位置放回屏幕内 / minimized with other windows, place is back on-screen");
                    }
                    else
                    {
                        Restore(h, true);
                        SetForegroundWindow(h);
                        notes.Add("点了任务栏上的 Claude，已从屏幕外拿回 / taskbar click, brought Claude back from off-screen");
                    }
                }
                Minimizing.Clear();
            }
            foreach (string note in notes) log?.Invoke(note);
        }

        static void Restore(IntPtr h, bool activate, bool keepMinimized = false)
        {
            WINDOWPLACEMENT original = Hidden[h];
            Hidden.Remove(h);
            bool maximized = original.showCmd == ShowMaximized || (original.showCmd == ShowMinimized && (original.flags & RestoreToMaximized) != 0);
            original.flags = keepMinimized && maximized ? RestoreToMaximized : 0;
            // A window Claude has hidden to its tray gets its place back but stays hidden.
            original.showCmd = !Native.IsWindowVisible(h) ? HideWindow
                : keepMinimized ? ShowMinNoActive
                : maximized ? ShowMaximized : activate ? ShowNormal : ShowNoActivate;
            SetWindowPlacement(h, ref original);
        }

        const int HideWindow = 0, ShowNormal = 1, ShowMinimized = 2, ShowMaximized = 3, ShowNoActivate = 4, ShowMinNoActive = 7;
        const int RestoreToMaximized = 2;
        static readonly IntPtr Bottom = new IntPtr(1);
        const uint NoSize = 0x1, NoMove = 0x2, NoActivate = 0x10;
        const uint EventForeground = 0x0003, EventMinimizeStart = 0x0016, OutOfContext = 0;

        delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        struct WINDOWPLACEMENT
        {
            public int length, flags, showCmd;
            public POINT ptMinPosition, ptMaxPosition;
            public RECT rcNormalPosition;
        }

        /// <summary>For the CI self-test: where the window's restored (normal) rectangle is, and whether it is minimized.</summary>
        public static string Describe(IntPtr h, out bool onScreen, out bool iconic)
        {
            var p = new WINDOWPLACEMENT { length = Marshal.SizeOf(typeof(WINDOWPLACEMENT)) };
            GetWindowPlacement(h, ref p);
            iconic = IsIconic(h);
            onScreen = p.rcNormalPosition.Left < SystemInformation.VirtualScreen.Right;
            bool tracked;
            lock (Gate) tracked = Hidden.ContainsKey(h);
            return $"iconic={iconic} normalLeft={p.rcNormalPosition.Left} showCmd={p.showCmd} tracked={tracked} foreground={GetForegroundWindow() == h}";
        }

        [DllImport("user32.dll")] static extern bool GetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT placement);
        [DllImport("user32.dll")] static extern bool SetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT placement);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint process, uint thread, uint flags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
    }
}
