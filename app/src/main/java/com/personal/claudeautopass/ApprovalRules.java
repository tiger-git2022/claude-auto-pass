package com.personal.claudeautopass;

import java.text.Normalizer;
import java.util.List;
import java.util.Locale;
import java.util.Set;

/** Deliberately exact labels: never approve arbitrary text containing "allow". */
public final class ApprovalRules {
    public static final String CLAUDE_PACKAGE = "com.anthropic.claude";
    public enum Kind { NONE, DENY, ONCE, SESSION }
    private static final Set<String> SESSION = new java.util.HashSet<>(java.util.Arrays.asList(
        "allow for this session", "always allow for this session", "always allow in this session",
        "allow for session", "allow this session", "allow for the session",
        "always allow for the session", "always allow in this conversation",
        "always allow for this conversation", "allow for this conversation",
        "本次会话中始终允许", "在本次会话中始终允许", "在此会话中始终允许",
        "本次会话始终允许", "始终允许本次会话", "在此对话中始终允许", "在本次对话中始终允许",
        "允许此会话", "允许本次会话", "在此工作阶段一律允许", "在本次工作階段中一律允許",
        "在此工作階段一律允許", "在此對話中一律允許", "在本次對話中一律允許"));
    private static final Set<String> ONCE = new java.util.HashSet<>(java.util.Arrays.asList(
        "allow once", "allow one time", "allow this time", "allow", "approve once",
        "允许一次", "仅允许一次", "仅此一次", "本次允许", "允许", "允許一次", "僅允許一次", "允許"));
    private static final Set<String> DENY = new java.util.HashSet<>(java.util.Arrays.asList(
        "deny", "reject", "decline", "don't allow", "do not allow", "cancel",
        "拒绝", "不允许", "取消", "拒絕", "不允許"));

    private ApprovalRules() {}
    public static String normalize(String label) {
        return Normalizer.normalize(label == null ? "" : label, Normalizer.Form.NFKC)
            .replace('\u2019', '\'').replaceAll("[\\p{Z}\\s]+", " ").trim().toLowerCase(Locale.ROOT);
    }
    public static Kind classify(String label) {
        String text = normalize(label);
        if (SESSION.contains(text)) return Kind.SESSION;
        if (ONCE.contains(text)) return Kind.ONCE;
        if (DENY.contains(text)) return Kind.DENY;
        return Kind.NONE;
    }
    public static boolean isApprovalGroup(List<Kind> kinds) {
        boolean once = kinds.contains(Kind.ONCE), session = kinds.contains(Kind.SESSION);
        return (once || session) && (kinds.contains(Kind.DENY) || (once && session));
    }
    public static int choose(List<Kind> kinds) {
        if (!isApprovalGroup(kinds)) return -1;
        int session = kinds.indexOf(Kind.SESSION);
        return session >= 0 ? session : kinds.indexOf(Kind.ONCE);
    }
    public static boolean canAct(String packageName, boolean enabled, boolean unlocked) {
        return CLAUDE_PACKAGE.equals(packageName) && enabled && unlocked;
    }
}
