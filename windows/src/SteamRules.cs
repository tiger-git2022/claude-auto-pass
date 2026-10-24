using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ClaudeAutoPass
{
    /// <summary>
    /// Rules for Steam Workshop's adult-content gate ("该物品包含您要求屏蔽的内容 … 查看物品 / 取消"), shown on
    /// steamcommunity.com in the Steam client or a browser. Only the gate's "view" button is pressed, and only when
    /// the same box also has a cancel button and the 18+ / mature-content text, so other "查看" buttons stay alone.
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
        /// The gate box: the age / blocked-content text and its two buttons. The box holds as many "view" labels as "cancel"
        /// labels (both buttons are built alike), so a box that also takes in some other "查看物品" on the page is rejected.
        /// </summary>
        public static bool IsGate(IEnumerable<string> texts)
        {
            int view = 0, cancel = 0;
            bool gate = false;
            foreach (string text in texts)
            {
                if (IsViewLabel(text)) view++;
                else if (IsCancelLabel(text)) cancel++;
                else if (IsGateText(text)) gate = true;
            }
            return view > 0 && view == cancel && gate;
        }
    }
}
