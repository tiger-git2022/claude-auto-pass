using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace ClaudeAutoPass
{
    /// <summary>
    /// "Claude 最小化时改放到最底层": Chromium stops updating a minimized page, so new approval cards never reach UI
    /// Automation, while a window that is merely covered stays current. A minimized Claude window is therefore restored
    /// behind every other window, but only while other windows cover it completely (so nothing pops up on the desktop);
    /// when it is uncovered again it is minimized back. Bringing it to the front yourself ends this for that window.
    /// </summary>
    sealed class BackParker
    {
        /// <summary>Windows restored behind the others by this class, with how many ticks in a row they were uncovered.</summary>
        readonly Dictionary<IntPtr, int> parked = new Dictionary<IntPtr, int>();

        /// <summary>One pass over Claude's windows; returns log lines for what changed.</summary>
        public List<string> Tick(IEnumerable<Native.Window> windows)
        {
            var notes = new List<string>();
            var seen = new HashSet<IntPtr>();
            IntPtr foreground = GetForegroundWindow();
            foreach (Native.Window w in windows)
            {
                if (!w.Visible) continue;
                seen.Add(w.Handle);
                if (Native.IsIconic(w.Handle))
                {
                    parked.Remove(w.Handle);
                    if (IsCovered(w.Handle) && Native.RestoreBehind(w.Handle))
                    {
                        parked[w.Handle] = 0;
                        notes.Add($"已把最小化的窗口放到其他窗口后面 / restored behind other windows \"{w.Title}\"");
                    }
                }
                else if (parked.TryGetValue(w.Handle, out int uncovered))
                {
                    if (w.Handle == foreground) parked.Remove(w.Handle); // brought to the front by the user
                    else if (IsCovered(w.Handle)) parked[w.Handle] = 0;
                    else if (uncovered + 1 < 2) parked[w.Handle] = uncovered + 1; // ignore a moment of Alt+Tab
                    else
                    {
                        parked.Remove(w.Handle);
                        ShowWindow(w.Handle, ShowMinNoActive);
                        notes.Add($"没有窗口挡住了，重新最小化 / uncovered, minimized again \"{w.Title}\"");
                    }
                }
            }
            foreach (IntPtr gone in new List<IntPtr>(parked.Keys))
                if (!seen.Contains(gone)) parked.Remove(gone);
            return notes;
        }

        const int ShowMinNoActive = 7;
        const int ExStyleIndex = -20;
        const int TransparentStyle = 0x20, ToolWindowStyle = 0x80;
        const int ExtendedFrameBounds = 9, Cloaked = 14;
        const int RestoreToMaximized = 2;
        const int Grid = 12, Inset = 10;

        static readonly HashSet<string> ShellClasses = new HashSet<string>(StringComparer.Ordinal)
        {
            "Progman", "WorkerW",
        };

        /// <summary>
        /// True when other windows hide the whole window (for a minimized one, the place it would be restored to). Only windows
        /// above it count (the taskbar does); the desktop, tool, click-through and cloaked windows don't. Parts outside every
        /// screen count as hidden.
        /// </summary>
        public static bool IsCovered(IntPtr target) => IsCovered(target, out _);

        public static bool IsCovered(IntPtr target, out string why)
        {
            why = null;
            bool iconic = Native.IsIconic(target);
            if (!TargetRect(target, iconic, out RECT area)) { why = "no rect"; return false; }
            area = new RECT { Left = area.Left + Inset, Top = area.Top + Inset, Right = area.Right - Inset, Bottom = area.Bottom - Inset };
            if (area.Right <= area.Left || area.Bottom <= area.Top) { why = "empty rect"; return false; }
            GetWindowThreadProcessId(target, out uint owner);

            var covers = new List<RECT>();
            EnumWindows((hwnd, _) =>
            {
                if (hwnd == target) return iconic; // windows below the target don't hide it
                if (!Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd)) return true;
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == owner) return true;
                int ex = GetWindowLong(hwnd, ExStyleIndex);
                var cls = new StringBuilder(64);
                GetClassName(hwnd, cls, cls.Capacity);
                string name = cls.ToString();
                if (ShellClasses.Contains(name)) return true;
                bool taskbar = name == "Shell_TrayWnd" || name == "Shell_SecondaryTrayWnd";
                if (!taskbar && (ex & (TransparentStyle | ToolWindowStyle)) != 0) return true;
                if (DwmGetWindowAttribute(hwnd, Cloaked, out int cloaked, 4) == 0 && cloaked != 0) return true;
                if (Bounds(hwnd, out RECT r) && r.Right > r.Left && r.Bottom > r.Top) covers.Add(r);
                return true;
            }, IntPtr.Zero);

            for (int i = 0; i <= Grid; i++)
                for (int j = 0; j <= Grid; j++)
                {
                    int x = area.Left + (area.Right - area.Left) * i / Grid;
                    int y = area.Top + (area.Bottom - area.Top) * j / Grid;
                    if (MonitorFromPoint(new POINT { X = x, Y = y }, 0 /* MONITOR_DEFAULTTONULL */) == IntPtr.Zero) continue;
                    if (!covers.Exists(r => x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom))
                    {
                        why = $"visible at {x},{y} of ({area.Left},{area.Top})-({area.Right},{area.Bottom}), {covers.Count} windows above";
                        return false;
                    }
                }
            return true;
        }

        /// <summary>Where the window is on screen, or for a minimized one, where restoring it would put it.</summary>
        static bool TargetRect(IntPtr hwnd, bool iconic, out RECT rect)
        {
            if (!iconic) return Bounds(hwnd, out rect);
            rect = default(RECT);
            var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf(typeof(WINDOWPLACEMENT)) };
            if (!GetWindowPlacement(hwnd, ref placement)) return false;
            RECT normal = placement.rcNormalPosition;
            var info = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            IntPtr monitor = MonitorFromRect(ref normal, 2 /* MONITOR_DEFAULTTONEAREST */);
            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) { rect = normal; return true; }
            if ((placement.flags & RestoreToMaximized) != 0) { rect = info.rcWork; return true; }
            // The normal position is in work-area coordinates (they differ when the taskbar is at the top or left).
            int dx = info.rcWork.Left - info.rcMonitor.Left, dy = info.rcWork.Top - info.rcMonitor.Top;
            rect = new RECT { Left = normal.Left + dx, Top = normal.Top + dy, Right = normal.Right + dx, Bottom = normal.Bottom + dy };
            return true;
        }

        /// <summary>The visible frame (without the invisible resize border), falling back to the window rectangle.</summary>
        static bool Bounds(IntPtr hwnd, out RECT rect)
        {
            if (DwmGetWindowAttribute(hwnd, ExtendedFrameBounds, out rect, Marshal.SizeOf(typeof(RECT))) == 0) return true;
            return GetWindowRect(hwnd, out rect);
        }

        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        struct WINDOWPLACEMENT
        {
            public int length, flags, showCmd;
            public POINT ptMinPosition, ptMaxPosition;
            public RECT rcNormalPosition;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor, rcWork;
            public int dwFlags;
        }

        delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] static extern bool GetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT placement);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT point, uint flags);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromRect(ref RECT rect, uint flags);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT value, int size);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
    }
}
