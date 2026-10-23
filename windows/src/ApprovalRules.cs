using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ClaudeAutoPass
{
    /// <summary>LocalProject: "Always allow, save to local project settings" on Claude Desktop's Code cards (chosen last).</summary>
    public enum Kind { None, Deny, Once, Session, LocalProject }

    /// <summary>
    /// Same rules as the Android app (app/src/main/java/.../ApprovalRules.java), plus labels used by
    /// Claude Desktop. Deliberately exact labels: never approve arbitrary text containing "allow".
    /// Permanent grants such as "Always allow" are never chosen, except the one the owner approved for Claude Desktop's
    /// Code cards: "Always allow, save to local project settings" (only this project's .claude/settings.local.json),
    /// used when the card offers nothing narrower.
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

        // "始终允许 , 保存到本地项目设置" / "Always allow, save to local project settings". Saving to shared project
        // settings or user settings is a wider grant and stays excluded.
        static readonly Regex LocalProjectLabel = new Regex(
            @"^(?:始终允许|始終允許|一律允許|always allow)\s*[,、:]?\s*\(?\s*(?:保存到本地项目设置|保存至本地项目设置|保存到本地項目設置|儲存到本機專案設定|儲存至本機專案設定|save to local project settings|saved to local project settings|save in local project settings)\s*\)?$",
            RegexOptions.CultureInvariant);

        // "始终允许 , 仅适用于本次会话" / "Always allow, only for this session": Claude Desktop's Code card scoped to the session.
        static readonly Regex SessionScopedLabel = new Regex(
            @"^(?:始终允许|始終允許|一律允許|always allow)\s*[,、:]?\s*\(?\s*(?:仅适用于本次会话|仅适用于此会话|仅限本次会话|僅適用於本次工作階段|僅適用於此工作階段|僅適用於本次會話|only for this session|for this session only|this session only|only this session)\s*\)?$",
            RegexOptions.CultureInvariant);

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
            if (SessionLabels.Contains(text) || SessionScopedLabel.IsMatch(text)) return Kind.Session;
            if (OnceLabels.Contains(text)) return Kind.Once;
            if (DenyLabels.Contains(text)) return Kind.Deny;
            if (LocalProjectLabel.IsMatch(text)) return Kind.LocalProject;
            return Kind.None;
        }

        /// <summary>A real prompt offers a positive choice next to a deny choice, or both one-time choices.</summary>
        public static bool IsApprovalGroup(IList<Kind> kinds)
        {
            bool once = kinds.Contains(Kind.Once), session = kinds.Contains(Kind.Session), local = kinds.Contains(Kind.LocalProject);
            return (once || session || local) && (kinds.Contains(Kind.Deny) || (once && session));
        }

        /// <summary>Higher wins: session-wide, then once, then the local-project grant.</summary>
        public static int Rank(Kind kind) => kind == Kind.Session ? 3 : kind == Kind.Once ? 2 : kind == Kind.LocalProject ? 1 : 0;

        /// <summary>Index of the button to press, or -1. Prefers session-wide approval.</summary>
        public static int Choose(IList<Kind> kinds)
        {
            if (!IsApprovalGroup(kinds)) return -1;
            int best = -1;
            for (int i = 0; i < kinds.Count; i++)
                if (Rank(kinds[i]) > 0 && (best < 0 || Rank(kinds[i]) > Rank(kinds[best]))) best = i;
            return best;
        }

        public static bool IsClaudeProcess(string processName) =>
            string.Equals(processName, ClaudeProcess, StringComparison.OrdinalIgnoreCase);

        public static bool CanAct(string processName, bool enabled, bool unlocked) =>
            IsClaudeProcess(processName) && enabled && unlocked;

        public static string Describe(Kind kind) =>
            kind == Kind.Session ? "已允许本次会话" : kind == Kind.Once ? "已允许一次" : kind == Kind.LocalProject ? "已始终允许（本地项目）" : "";
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
