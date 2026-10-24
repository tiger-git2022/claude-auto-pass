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

        public static bool Any { get { lock (Gate) return Hidden.Count > 0; } }

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

        /// <summary>Called by the scan loop: a hidden window the user switched to (taskbar, Alt+Tab) comes back.</summary>
        public static List<string> Tick()
        {
            var notes = new List<string>();
            lock (Gate)
            {
                if (Hidden.Count == 0) return notes;
                IntPtr foreground = GetForegroundWindow();
                foreach (IntPtr h in new List<IntPtr>(Hidden.Keys))
                {
                    if (!Native.IsWindowVisible(h)) { Hidden.Remove(h); continue; }
                    // Claude may activate itself when restored from minimized; give that a moment to settle.
                    if (h == foreground && (DateTime.UtcNow - hiddenAt).TotalSeconds > 2)
                    {
                        Restore(h, true);
                        notes.Add("切到 Claude，已从屏幕外拿回 / brought Claude back from off-screen");
                    }
                }
            }
            return notes;
        }

        static void Restore(IntPtr h, bool activate)
        {
            WINDOWPLACEMENT original = Hidden[h];
            Hidden.Remove(h);
            original.flags = 0;
            original.showCmd = original.showCmd == ShowMaximized ? ShowMaximized : activate ? ShowNormal : ShowNoActivate;
            SetWindowPlacement(h, ref original);
        }

        const int ShowNormal = 1, ShowMaximized = 3, ShowNoActivate = 4;

        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        struct WINDOWPLACEMENT
        {
            public int length, flags, showCmd;
            public POINT ptMinPosition, ptMaxPosition;
            public RECT rcNormalPosition;
        }

        [DllImport("user32.dll")] static extern bool GetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT placement);
        [DllImport("user32.dll")] static extern bool SetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT placement);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    }
}
