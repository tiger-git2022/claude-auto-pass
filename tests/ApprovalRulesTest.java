package com.personal.claudeautopass;
import java.util.*;
public final class ApprovalRulesTest {
    private static int assertions;
    private static void check(boolean condition, String name) {
        assertions++; if (!condition) throw new AssertionError(name);
    }
    public static void main(String[] args) {
        check(ApprovalRules.classify("在本次会话中始终允许") == ApprovalRules.Kind.SESSION, "Chinese session");
        check(ApprovalRules.classify("  Always ALLOW for this session\n") == ApprovalRules.Kind.SESSION, "English session normalization");
        check(ApprovalRules.classify("允許一次") == ApprovalRules.Kind.ONCE, "Traditional Chinese once");
        check(ApprovalRules.classify("Don’t allow") == ApprovalRules.Kind.DENY, "Curly apostrophe deny");
        check(ApprovalRules.choose(Arrays.asList(ApprovalRules.Kind.DENY, ApprovalRules.Kind.ONCE, ApprovalRules.Kind.SESSION)) == 2, "Prefer session");
        check(ApprovalRules.choose(Arrays.asList(ApprovalRules.Kind.SESSION, ApprovalRules.Kind.ONCE, ApprovalRules.Kind.DENY)) == 0, "Order independent");
        check(ApprovalRules.choose(Arrays.asList(ApprovalRules.Kind.DENY, ApprovalRules.Kind.ONCE)) == 1, "Once fallback");
        check(ApprovalRules.choose(Arrays.asList(ApprovalRules.Kind.ONCE, ApprovalRules.Kind.SESSION)) == 1, "Two positive alternatives");
        check(ApprovalRules.choose(Collections.singletonList(ApprovalRules.Kind.ONCE)) == -1, "Ignore isolated allow");
        check(ApprovalRules.choose(Collections.singletonList(ApprovalRules.Kind.DENY)) == -1, "Never deny");
        for (String text : Arrays.asList("Always allow", "Always allow everything", "Allow once please", "The button says Allow once", "Yes", "Continue", "Approve", "", "不允许本次会话", "Allow all future sessions"))
            check(ApprovalRules.classify(text) == ApprovalRules.Kind.NONE, "Reject ambiguous or quoted text: " + text);
        check(!ApprovalRules.canAct("com.android.permissioncontroller", true, true), "Never approve Android permissions");
        check(!ApprovalRules.canAct("com.android.systemui", true, true), "Ignore notification shade");
        check(!ApprovalRules.canAct("com.anthropic.claude.fake", true, true), "Exact package only");
        check(!ApprovalRules.canAct(ApprovalRules.CLAUDE_PACKAGE, false, true), "Pause gate");
        check(!ApprovalRules.canAct(ApprovalRules.CLAUDE_PACKAGE, true, false), "Lock gate");
        check(ApprovalRules.canAct(ApprovalRules.CLAUDE_PACKAGE, true, true), "Active unlocked Claude");
        System.out.println("Passed " + assertions + " approval rule checks.");
    }
}
