using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ClaudeAutoPass
{
    public enum Kind { None, Deny, Once, Session }

    /// <summary>
    /// Same rules as the Android app (app/src/main/java/.../ApprovalRules.java), plus labels used by
    /// Claude Desktop. Deliberately exact labels: never approve arbitrary text containing "allow".
    /// Permanent grants such as "Always allow" are never chosen.
    /// </summary>
    public static class ApprovalRules
    {
        /// <summary>Process name of Claude Desktop (claude.exe), compared without ".exe".</summary>
        public const string ClaudeProcess = "claude";

        static readonly HashSet<string> SessionLabels = new HashSet<string>(StringComparer.Ordinal)
        {
            "allow for this session", "always allow for this session", "always allow in this session",
            "allow for session", "allow this session", "allow for the session",
            "always allow for the session", "always allow in this conversation",
            "always allow for this conversation", "allow for this conversation",
            "本次会话中始终允许", "在本次会话中始终允许", "在此会话中始终允许",
            "本次会话始终允许", "始终允许本次会话", "在此对话中始终允许", "在本次对话中始终允许",
            "允许此会话", "允许本次会话", "在此工作阶段一律允许", "在本次工作階段中一律允許",
            "在此工作階段一律允許", "在此對話中一律允許", "在本次對話中一律允許",
            // Claude Desktop (tool permission prompts are scoped to a chat).
            "allow for this chat", "always allow for this chat", "always allow in this chat",
            "在此聊天中始终允许", "在本次聊天中始终允许", "允许此聊天", "允许本次聊天",
            "在此聊天中一律允許", "在本次聊天中一律允許",
        };

        static readonly HashSet<string> OnceLabels = new HashSet<string>(StringComparer.Ordinal)
        {
            "allow once", "allow one time", "allow this time", "allow", "approve once",
            "允许一次", "仅允许一次", "仅此一次", "本次允许", "允许", "允許一次", "僅允許一次", "允許",
        };

        static readonly HashSet<string> DenyLabels = new HashSet<string>(StringComparer.Ordinal)
        {
            "deny", "reject", "decline", "don't allow", "do not allow", "cancel",
            "拒绝", "不允许", "取消", "拒絕", "不允許",
        };

        const string KeyToken = @"(?:ctrl|control|alt|shift|enter|return|esc|escape|cmd|⌘|⌥|⇧|⌃|⏎|↵|↩)";
        // Trailing keyboard hints that Electron apps sometimes put into a button's accessible name,
        // e.g. "Deny Esc", "Allow once ⏎", "Allow once (Ctrl+Enter)".
        static readonly Regex ShortcutHint = new Regex(
            @"(?:\s*\(\s*" + KeyToken + @"(?:\s*\+\s*(?:" + KeyToken + @"|[a-z0-9]))*\s*\)|\s+" + KeyToken + @"(?:\s*\+\s*(?:" + KeyToken + @"|[a-z0-9]))*|\s*[⌘⌥⇧⌃⏎↵↩]+)$",
            RegexOptions.CultureInvariant);
        static readonly Regex Spaces = new Regex(@"[\p{Z}\s]+", RegexOptions.CultureInvariant);

        public static string Normalize(string label)
        {
            string text = (label ?? "").Normalize(NormalizationForm.FormKC).Replace('’', '\'');
            text = Spaces.Replace(text, " ").Trim().ToLowerInvariant();
            string stripped = ShortcutHint.Replace(text, "").Trim();
            return stripped.Length > 0 ? stripped : text;
        }

        public static Kind Classify(string label)
        {
            string text = Normalize(label);
            if (SessionLabels.Contains(text)) return Kind.Session;
            if (OnceLabels.Contains(text)) return Kind.Once;
            if (DenyLabels.Contains(text)) return Kind.Deny;
            return Kind.None;
        }

        /// <summary>A real prompt offers a positive choice next to a deny choice, or both positive choices.</summary>
        public static bool IsApprovalGroup(IList<Kind> kinds)
        {
            bool once = kinds.Contains(Kind.Once), session = kinds.Contains(Kind.Session);
            return (once || session) && (kinds.Contains(Kind.Deny) || (once && session));
        }

        /// <summary>Index of the button to press, or -1. Prefers session-wide approval.</summary>
        public static int Choose(IList<Kind> kinds)
        {
            if (!IsApprovalGroup(kinds)) return -1;
            int session = kinds.IndexOf(Kind.Session);
            return session >= 0 ? session : kinds.IndexOf(Kind.Once);
        }

        public static bool IsClaudeProcess(string processName) =>
            string.Equals(processName, ClaudeProcess, StringComparison.OrdinalIgnoreCase);

        public static bool CanAct(string processName, bool enabled, bool unlocked) =>
            IsClaudeProcess(processName) && enabled && unlocked;

        public static string Describe(Kind kind) =>
            kind == Kind.Session ? "已允许本次会话" : kind == Kind.Once ? "已允许一次" : "";
    }

    /// <summary>Windows release tags look like win-v1.0.0-t12.</summary>
    public static class Versioning
    {
        static readonly Regex Tag = new Regex(@"^win-v(\d+)\.(\d+)\.(\d+)-t(\d+)$", RegexOptions.CultureInvariant);
        static readonly Regex Plain = new Regex(@"^v?(\d+)\.(\d+)\.(\d+)-t(\d+)$", RegexOptions.CultureInvariant);

        /// <summary>Parses "win-v1.2.3-t4" (or "1.2.3-t4") into comparable parts; null when invalid.</summary>
        public static int[] Parse(string tag, bool requireWindowsPrefix)
        {
            Match m = (requireWindowsPrefix ? Tag : Plain).Match(tag ?? "");
            if (!m.Success && !requireWindowsPrefix) m = Tag.Match(tag ?? "");
            if (!m.Success) return null;
            return Enumerable.Range(1, 4).Select(i => int.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture)).ToArray();
        }

        public static int Compare(int[] a, int[] b)
        {
            for (int i = 0; i < 4; i++)
            {
                int c = a[i].CompareTo(b[i]);
                if (c != 0) return c;
            }
            return 0;
        }

        /// <summary>True when a release tag is newer than the running version (a dev build "-t0" is older than any release).</summary>
        public static bool IsNewer(string releaseTag, string currentVersion)
        {
            int[] release = Parse(releaseTag, true), current = Parse(currentVersion, false);
            if (release == null) return false;
            return current == null || Compare(release, current) > 0;
        }
    }
}
