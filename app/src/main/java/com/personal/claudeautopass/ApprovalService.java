package com.personal.claudeautopass;

import android.accessibilityservice.AccessibilityService;
import android.app.KeyguardManager;
import android.content.*;
import android.util.Log;
import android.os.*;
import android.view.accessibility.*;
import java.util.*;

public final class ApprovalService extends AccessibilityService {
    private static ApprovalService instance;
    private final Handler handler = new Handler(Looper.getMainLooper());
    private long lastClick;
    @Override protected void dump(java.io.FileDescriptor fd, java.io.PrintWriter writer, String[] args) {
        writer.println("pid=" + android.os.Process.myPid() + " connected=" + connected() + " currentInstance=" + (instance == this));
        writer.println("serviceInfo=" + (getServiceInfo() != null) + " granted=" + AccessibilityState.granted(this));
        writer.println("running=" + Control.running(this) + " requestedEnabled=" + Control.enabled(this));
    }
    private final Runnable scanLoop = new Runnable() {
        @Override public void run() {
            if (Control.enabled(ApprovalService.this)) safeScan();
            handler.postDelayed(this, 800);
        }
    };
    static boolean connected() { return instance != null; }
    static void exit(Context c) {
        Control.setEnabled(c, false);
        Control.exit(c);
    }
    @Override protected void onServiceConnected() {
        instance = this;
        Log.i("ClaudeAutoPass", "Accessibility connected pid=" + android.os.Process.myPid());
        Control.refresh(this);
        handler.removeCallbacksAndMessages(null);
        handler.post(scanLoop);
    }
    @Override public void onAccessibilityEvent(AccessibilityEvent event) {
        // A short settle time lets all approval choices appear before selecting one.
        if (Control.enabled(this)) { handler.removeCallbacks(eventScan); handler.postDelayed(eventScan, 280); }
    }
    private final Runnable eventScan = this::safeScan;
    @Override public void onInterrupt() { Log.i("ClaudeAutoPass", "Feedback interrupted; preserving user switch"); }
    @Override public boolean onUnbind(Intent intent) { disconnect(); return super.onUnbind(intent); }
    @Override public void onDestroy() { disconnect(); super.onDestroy(); }
    private void disconnect() {
        handler.removeCallbacksAndMessages(null);
        if (instance == this) instance = null;
        Log.i("ClaudeAutoPass", "Accessibility disconnected pid=" + android.os.Process.myPid());
        Control.refresh(this);
    }
    private void safeScan() {
        try { scan(); }
        catch (IllegalStateException | SecurityException e) {
            Log.w("ClaudeAutoPass", "Transient accessibility scan failure: " + e.getClass().getSimpleName());
        }
    }
    private static final class Entry {
        final AccessibilityNodeInfo node;
        final List<Integer> ancestors;
        final ApprovalRules.Kind kind;
        Entry(AccessibilityNodeInfo node, List<Integer> ancestors, ApprovalRules.Kind kind) {
            this.node = node; this.ancestors = ancestors; this.kind = kind;
        }
    }
    private void scan() {
        if (!Control.enabled(this) || SystemClock.elapsedRealtime() - lastClick < 1500) return;
        KeyguardManager keyguard = getSystemService(KeyguardManager.class);
        if (keyguard.isKeyguardLocked() || !getSystemService(PowerManager.class).isInteractive()) return;
        AccessibilityNodeInfo root = getRootInActiveWindow();
        if (root == null) return;
        List<AccessibilityNodeInfo> nodes = new ArrayList<>();
        try {
            if (!ApprovalRules.canAct(String.valueOf(root.getPackageName()), Control.enabled(this), true)) return;
            List<Entry> choices = new ArrayList<>();
            collect(root, new ArrayList<>(), nodes, choices, 0);
            // Prefer SESSION globally among candidates belonging to a nearby approval group.
            Entry selected = null;
            for (Entry entry : choices) {
                if (entry.kind != ApprovalRules.Kind.SESSION && entry.kind != ApprovalRules.Kind.ONCE) continue;
                if (!inApprovalGroup(entry, choices)) continue;
                if (selected == null || entry.kind == ApprovalRules.Kind.SESSION) selected = entry;
                if (selected.kind == ApprovalRules.Kind.SESSION) break;
            }
            if (selected != null && Control.enabled(this) && selected.node.refresh()
                && selected.node.isVisibleToUser() && selected.node.isEnabled()
                && ApprovalRules.CLAUDE_PACKAGE.contentEquals(selected.node.getPackageName() == null ? "" : selected.node.getPackageName())) {
                if (selected.node.performAction(AccessibilityNodeInfo.ACTION_CLICK)) {
                    lastClick = SystemClock.elapsedRealtime();
                    Control.prefs(this).edit().putString("last_action", selected.kind == ApprovalRules.Kind.SESSION ? "已允许本次会话" : "已允许一次")
                        .putLong("last_action_time", System.currentTimeMillis()).apply();
                }
            }
        } finally {
            for (int i = nodes.size() - 1; i > 0; i--) nodes.get(i).recycle();
            root.recycle();
        }
    }
    private void collect(AccessibilityNodeInfo node, List<Integer> parents, List<AccessibilityNodeInfo> nodes,
                         List<Entry> choices, int depth) {
        if (nodes.size() >= 1800 || depth > 50) return;
        int index = nodes.size(); nodes.add(node);
        if (node.isVisibleToUser() && node.isEnabled() && node.isClickable()) {
            Set<String> labels = new HashSet<>();
            buttonLabels(node, labels, 0);
            ApprovalRules.Kind kind = ApprovalRules.Kind.NONE;
            boolean valid = !labels.isEmpty();
            for (String label : labels) {
                ApprovalRules.Kind current = ApprovalRules.classify(label);
                if (current == ApprovalRules.Kind.NONE || (kind != ApprovalRules.Kind.NONE && kind != current)) { valid = false; break; }
                kind = current;
            }
            if (valid) choices.add(new Entry(node, new ArrayList<>(parents), kind));
        }
        List<Integer> ancestors = new ArrayList<>(parents); ancestors.add(index);
        for (int i = 0; i < node.getChildCount() && nodes.size() < 1800; i++) {
            AccessibilityNodeInfo child = node.getChild(i);
            if (child != null) {
                if (depth < 50) collect(child, ancestors, nodes, choices, depth + 1);
                else child.recycle();
            }
        }
    }
    private void buttonLabels(AccessibilityNodeInfo node, Set<String> labels, int depth) {
        if (depth > 4) { labels.add("__too_deep__"); return; }
        if (node.getText() != null && !node.getText().toString().trim().isEmpty()) labels.add(ApprovalRules.normalize(node.getText().toString()));
        if (node.getContentDescription() != null && !node.getContentDescription().toString().trim().isEmpty()) labels.add(ApprovalRules.normalize(node.getContentDescription().toString()));
        for (int i = 0; i < node.getChildCount(); i++) {
            AccessibilityNodeInfo child = node.getChild(i);
            if (child == null) continue;
            try {
                if (child.isVisibleToUser()) {
                    if (child.isClickable()) { labels.add("__nested_button__"); continue; }
                    buttonLabels(child, labels, depth + 1);
                }
            } finally { child.recycle(); }
        }
    }
    private boolean inApprovalGroup(Entry candidate, List<Entry> entries) {
        // Stay close to the buttons, rather than combining unrelated controls across a screen.
        for (int p = candidate.ancestors.size() - 1, distance = 0; p >= 0 && distance < 5; p--, distance++) {
            int ancestor = candidate.ancestors.get(p);
            if (ancestor == 0) continue;
            List<ApprovalRules.Kind> kinds = new ArrayList<>();
            for (Entry other : entries) if (other.ancestors.contains(ancestor)) kinds.add(other.kind);
            if (ApprovalRules.isApprovalGroup(kinds)) return true;
        }
        return false;
    }
}
