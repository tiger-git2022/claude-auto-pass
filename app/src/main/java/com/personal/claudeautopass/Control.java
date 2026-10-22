package com.personal.claudeautopass;

import android.Manifest;
import android.app.*;
import android.content.*;
import android.content.pm.PackageManager;
import android.os.Build;
import android.service.quicksettings.TileService;

final class Control {
    private static final String CHANNEL = "approval_control";
    static final String ENABLED = "enabled", RUNNING = "running";
    static final String ACTION_TOGGLE = "com.personal.claudeautopass.TOGGLE";
    static final String ACTION_EXIT = "com.personal.claudeautopass.EXIT";
    private static final int NOTIFICATION = 7;
    static android.content.SharedPreferences prefs(Context c) { return c.getSharedPreferences("control", Context.MODE_PRIVATE); }
    static boolean running(Context c) { return prefs(c).getBoolean(RUNNING, false); }
    static boolean enabled(Context c) { return running(c) && prefs(c).getBoolean(ENABLED, false); }
    static void open(Context c) { prefs(c).edit().putBoolean(RUNNING, true).apply(); refresh(c); }
    static void exit(Context c) { prefs(c).edit().putBoolean(ENABLED, false).putBoolean(RUNNING, false).apply(); refresh(c); }
    static void setEnabled(Context c, boolean enabled) {
        android.content.SharedPreferences.Editor editor = prefs(c).edit().putBoolean(ENABLED, enabled && ApprovalService.connected());
        if (enabled) editor.putBoolean(RUNNING, true);
        editor.apply(); refresh(c);
    }
    static void refresh(Context c) {
        TileService.requestListeningState(c, new ComponentName(c, ApprovalTileService.class));
        NotificationManager manager = c.getSystemService(NotificationManager.class);
        manager.createNotificationChannel(new NotificationChannel(CHANNEL, "自动审批开关", NotificationManager.IMPORTANCE_LOW));
        if (!ApprovalService.connected() || !running(c)) { manager.cancel(NOTIFICATION); return; }
        if (Build.VERSION.SDK_INT >= 33 && c.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) return;
        boolean on = enabled(c);
        PendingIntent toggle = PendingIntent.getBroadcast(c, 1, new Intent(c, ToggleReceiver.class).setAction(ACTION_TOGGLE), PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
        PendingIntent exit = PendingIntent.getBroadcast(c, 3, new Intent(c, ToggleReceiver.class).setAction(ACTION_EXIT), PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
        PendingIntent open = PendingIntent.getActivity(c, 2, new Intent(c, MainActivity.class), PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
        Notification notification = new Notification.Builder(c, CHANNEL)
            .setSmallIcon(R.drawable.ic_approval).setContentTitle(on ? "Claude 自动审批 · 已开启" : "Claude 自动审批 · 已关闭")
            .setContentText(on ? "优先允许本次会话，其次允许一次" : "点击“开启”恢复自动审批")
            .setContentIntent(open).setOngoing(true).setOnlyAlertOnce(true).setShowWhen(false)
            .addAction(new Notification.Action.Builder(null, on ? "关闭" : "开启", toggle).build())
            .addAction(new Notification.Action.Builder(null, "退出", exit).build()).build();
        manager.notify(NOTIFICATION, notification);
    }
}
