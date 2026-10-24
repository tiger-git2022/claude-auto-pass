using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ClaudeAutoPass
{
    /// <summary>
    /// Rules for Steam Workshop's adult-content gate ("该物品包含您要求屏蔽的内容 … 查看物品 / 取消"), shown on
    /// steamcommunity.com in the Steam client or a browser. Only the gate's "view" button is pressed, and only when
    /// a cancel button sits right next to it and the 18+ / mature-content text right above, so other "查看" buttons stay alone.
    /// </summary>
    public static class SteamRules
    {
        /// <summary>Exact button texts (as Steam shows them) for "view this item / page" on the gate.</summary>
        public static readonly string[] ViewLabels =
        {
            "查看物品", "查看页面", "查看内容", "查看項目", "查看頁面", "檢視物品", "檢視項目", "檢視頁面", "檢視內容",
            "View Item", "View Page", "View Content", "View item", "View page", "View content",
        };

        public static readonly string[] CancelLabels = { "取消", "Cancel" };

        static readonly HashSet<string> View = new HashSet<string>(ViewLabels.Select(ApprovalRules.Normalize), StringComparer.Ordinal);
        static readonly HashSet<string> Cancel = new HashSet<string>(CancelLabels.Select(ApprovalRules.Normalize), StringComparer.Ordinal);

        // The age confirmation or the blocked-content reason that Steam prints above the buttons.
        static readonly Regex GateText = new Regex(
            @"18\s*(?:周岁|岁|週歲|歲|years?|\+)|至少年满|已年满|已滿|至少年滿|成人内容|成人內容|裸露|色情|要求屏蔽的内容|要求封鎖的內容|内容偏好|內容偏好|" +
            @"mature content|adult content|nudity|sexual content|content preferences|you have requested to hide|at least 18",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>Steam client windows (CEF) are owned by steamwebhelper.exe.</summary>
        static readonly HashSet<string> SteamProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "steamwebhelper", "steam" };

        static readonly HashSet<string> BrowserProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "msedge", "chrome", "firefox", "brave", "opera", "vivaldi", "360se", "360chrome", "qqbrowser", "sogouexplorer",
        };

        public static bool IsViewLabel(string label) => View.Contains(ApprovalRules.Normalize(label));

        public static bool IsCancelLabel(string label) => Cancel.Contains(ApprovalRules.Normalize(label));

        public static bool IsGateText(string text) => !string.IsNullOrEmpty(text) && GateText.IsMatch(ApprovalRules.Normalize(text));

        public static bool IsSteamProcess(string processName) => SteamProcesses.Contains(processName ?? "");

        public static bool IsBrowserProcess(string processName) => BrowserProcesses.Contains(processName ?? "");

        public static bool IsTargetProcess(string processName) => IsSteamProcess(processName) || IsBrowserProcess(processName);

        /// <summary>Browser windows are only scanned while the open tab is a Steam page (keeps scans of other sites free).</summary>
        public static bool IsSteamPageTitle(string title)
        {
            string t = (title ?? "").ToLowerInvariant();
            return t.Contains("steam") || t.Contains("创意工坊") || t.Contains("創意工坊");
        }

        /// <summary>
        /// "取消" sits on the same row as "查看物品", right next to it (screen pixels). Steam's buttons are plain clickable
        /// boxes whose UI Automation parents don't reliably frame the dialog, so the gate is recognised by layout instead.
        /// </summary>
        public static bool IsButtonPair(Box view, Box cancel)
        {
            if (view.IsEmpty || cancel.IsEmpty) return false;
            int tolerance = Math.Max(12, Math.Max(view.Height, cancel.Height));
            if (Math.Abs(view.CenterY - cancel.CenterY) > tolerance) return false;
            int gap = cancel.Left >= view.Left ? cancel.Left - view.Right : view.Left - cancel.Right;
            return gap >= -4 && gap <= 400;
        }

        /// <summary>The 18+ / blocked-content text is just above the two buttons, roughly centred over them.</summary>
        public static bool IsAbove(Box buttons, Box text)
        {
            if (buttons.IsEmpty || text.IsEmpty) return false;
            int distance = buttons.Top - text.Bottom;
            return distance >= -4 && distance <= 500 && text.CenterX >= buttons.Left - 700 && text.CenterX <= buttons.Right + 700;
        }

        public static Box Union(Box a, Box b) => new Box(Math.Min(a.Left, b.Left), Math.Min(a.Top, b.Top), Math.Max(a.Right, b.Right), Math.Max(a.Bottom, b.Bottom));
    }

    /// <summary>A screen rectangle in pixels.</summary>
    public struct Box
    {
        public readonly int Left, Top, Right, Bottom;
        public Box(int left, int top, int right, int bottom) { Left = left; Top = top; Right = right; Bottom = bottom; }
        public int Width => Right - Left;
        public int Height => Bottom - Top;
        public int CenterX => (Left + Right) / 2;
        public int CenterY => (Top + Bottom) / 2;
        public bool IsEmpty => Width <= 0 || Height <= 0;
        public override string ToString() => $"({Left},{Top} {Width}x{Height})";
    }
}
