package com.personal.claudeautopass;

import android.accessibilityservice.AccessibilityServiceInfo;
import android.content.*;
import android.provider.Settings;
import android.view.accessibility.AccessibilityManager;

final class AccessibilityState {
    static boolean granted(Context context) {
        if (ApprovalService.connected()) return true;
        ComponentName expected = new ComponentName(context, ApprovalService.class);
        // Some ROMs return a filtered/delayed AccessibilityManager list during transitions.
        // Read the selected component independently; this never changes system settings.
        try {
            String selected = Settings.Secure.getString(context.getContentResolver(), Settings.Secure.ENABLED_ACCESSIBILITY_SERVICES);
            if (selected != null) for (String component : selected.split(":")) {
                if (expected.equals(ComponentName.unflattenFromString(component))) return true;
            }
        } catch (SecurityException ignored) { }
        AccessibilityManager manager = context.getSystemService(AccessibilityManager.class);
        for (AccessibilityServiceInfo info : manager.getEnabledAccessibilityServiceList(AccessibilityServiceInfo.FEEDBACK_ALL_MASK)) {
            android.content.pm.ServiceInfo service = info.getResolveInfo().serviceInfo;
            ComponentName component = ComponentName.unflattenFromString(service.packageName + "/" + service.name);
            if (expected.equals(component)) return true;
        }
        return false;
    }
}
