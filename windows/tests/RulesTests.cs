using System;
using System.Collections.Generic;
using ClaudeAutoPass;

static class RulesTests
{
    static int assertions;
    static void Check(bool condition, string name)
    {
        assertions++;
        if (!condition) throw new Exception("FAILED: " + name);
    }

    static int Main()
    {
        // Same cases as tests/ApprovalRulesTest.java.
        Check(ApprovalRules.Classify("在本次会话中始终允许") == Kind.Session, "Chinese session");
        Check(ApprovalRules.Classify("  Always ALLOW for this session\n") == Kind.Session, "English session normalization");
        Check(ApprovalRules.Classify("允許一次") == Kind.Once, "Traditional Chinese once");
        Check(ApprovalRules.Classify("Don’t allow") == Kind.Deny, "Curly apostrophe deny");
        Check(ApprovalRules.Choose(new List<Kind> { Kind.Deny, Kind.Once, Kind.Session }) == 2, "Prefer session");
        Check(ApprovalRules.Choose(new List<Kind> { Kind.Session, Kind.Once, Kind.Deny }) == 0, "Order independent");
        Check(ApprovalRules.Choose(new List<Kind> { Kind.Deny, Kind.Once }) == 1, "Once fallback");
        Check(ApprovalRules.Choose(new List<Kind> { Kind.Once, Kind.Session }) == 1, "Two positive alternatives");
        Check(ApprovalRules.Choose(new List<Kind> { Kind.Once }) == -1, "Ignore isolated allow");
        Check(ApprovalRules.Choose(new List<Kind> { Kind.Deny }) == -1, "Never deny");
        Check(ApprovalRules.Choose(new List<Kind> { Kind.None, Kind.Deny }) == -1, "Never choose permanent grant");
        foreach (string text in new[] { "Always allow", "Always allow everything", "Allow once please", "The button says Allow once",
                     "Yes", "Continue", "Approve", "", "不允许本次会话", "Allow all future sessions", "始终允许", "Allow always" })
            Check(ApprovalRules.Classify(text) == Kind.None, "Reject ambiguous or quoted text: " + text);

        // Claude Desktop specifics.
        Check(ApprovalRules.Classify("Allow for This Chat") == Kind.Session, "Desktop chat-scoped approval");
        Check(ApprovalRules.Classify("Allow once ⏎") == Kind.Once, "Strip enter glyph");
        Check(ApprovalRules.Classify("Allow once (Ctrl+Enter)") == Kind.Once, "Strip shortcut in parentheses");
        Check(ApprovalRules.Classify("Deny Esc") == Kind.Deny, "Strip escape hint");
        Check(ApprovalRules.Classify("Always allow ⌘⏎") == Kind.None, "Shortcut does not make permanent grant acceptable");
        Check(ApprovalRules.Classify("Allow once now") == Kind.None, "Only keyboard hints are stripped");

        // Claude Desktop Code cards: "Deny | Always allow, save to local project settings ⌄" (owner-approved grant).
        Check(ApprovalRules.Classify("始终允许 , 保存到本地项目设置") == Kind.LocalProject, "Local project grant (zh)");
        Check(ApprovalRules.Classify("始终允许，保存到本地项目设置 Ctrl+Shift+⏎") == Kind.LocalProject, "Local project grant with shortcut");
        Check(ApprovalRules.Classify("Always allow, save to local project settings") == Kind.LocalProject, "Local project grant (en)");
        Check(ApprovalRules.Classify("始终允许 , 保存到用户设置") == Kind.None, "User-wide grant is never chosen");
        Check(ApprovalRules.Classify("Always allow, save to project settings") == Kind.None, "Shared project grant is never chosen");
        Check(ApprovalRules.Classify("更多允许选项") == Kind.None, "Dropdown arrow is not a choice");
        Check(ApprovalRules.Choose(new List<Kind> { Kind.Deny, Kind.LocalProject, Kind.None }) == 1, "Local project grant next to deny");
        Check(ApprovalRules.Choose(new List<Kind> { Kind.LocalProject }) == -1, "Isolated local project grant");
        Check(ApprovalRules.Choose(new List<Kind> { Kind.Deny, Kind.LocalProject, Kind.Once }) == 2, "One-time beats local project grant");
        Check(ApprovalRules.Choose(new List<Kind> { Kind.Deny, Kind.Session, Kind.LocalProject }) == 1, "Session beats local project grant");

        Check(ApprovalRules.IsClaudeProcess("claude") && ApprovalRules.IsClaudeProcess("Claude"), "Claude Desktop process");
        Check(!ApprovalRules.IsClaudeProcess("claude-helper") && !ApprovalRules.IsClaudeProcess("msedge") && !ApprovalRules.IsClaudeProcess("consent"), "Other processes");
        Check(!ApprovalRules.CanAct("claude", false, true), "Pause gate");
        Check(!ApprovalRules.CanAct("claude", true, false), "Lock gate");
        Check(ApprovalRules.CanAct("claude", true, true), "Active unlocked Claude");

        Check(Versioning.IsNewer("win-v1.0.0-t3", "1.0.0-t2"), "Newer build");
        Check(!Versioning.IsNewer("win-v1.0.0-t2", "1.0.0-t2"), "Same build");
        Check(!Versioning.IsNewer("win-v1.0.0-t9", "1.0.1-t1"), "Older base");
        Check(Versioning.IsNewer("win-v1.0.0-t1", "1.0.0-t0"), "Any release beats a dev build");
        Check(!Versioning.IsNewer("v1.0.0-t9", "1.0.0-t1"), "Android tags are ignored");
        Check(Versioning.IsNewer("win-v1.0.0-t10", "1.0.0-t9"), "Numeric, not lexical");

        Console.WriteLine("Passed " + assertions + " Windows approval rule checks.");
        return 0;
    }
}
