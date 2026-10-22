using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace ClaudeAutoPass
{
    static class Native
    {
        delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);

        public sealed class Window
        {
            public IntPtr Handle;
            public int ProcessId;
            public string ClassName;
            public string Title;
            public bool Visible;
        }

        /// <summary>Top-level windows owned by the given processes.</summary>
        public static List<Window> TopLevelWindows(ISet<int> processIds)
        {
            var result = new List<Window>();
            EnumWindows((hwnd, _) =>
            {
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (processIds.Contains((int)pid))
                {
                    var cls = new StringBuilder(256);
                    GetClassName(hwnd, cls, cls.Capacity);
                    var title = new StringBuilder(512);
                    GetWindowText(hwnd, title, title.Capacity);
                    result.Add(new Window { Handle = hwnd, ProcessId = (int)pid, ClassName = cls.ToString(), Title = title.ToString(), Visible = IsWindowVisible(hwnd) });
                }
                return true;
            }, IntPtr.Zero);
            return result;
        }
    }
}
