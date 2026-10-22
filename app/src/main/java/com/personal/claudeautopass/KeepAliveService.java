package com.personal.claudeautopass;

import android.app.*;
import android.content.*;
import android.content.pm.ServiceInfo;
import android.os.*;
import android.util.Log;

/** Visible, user-controlled foreground service; never changes accessibility permission. */
public final class KeepAliveService extends Service {
    private static KeepAliveService instance;
    static void sync(Context context) {
        if (!Control.running(context)) {
            context.stopService(new Intent(context, KeepAliveService.class));
            context.getSystemService(NotificationManager.class).cancel(Control.NOTIFICATION);
            return;
        }
        if (instance != null) { instance.show(); return; }
        try {
            context.startForegroundService(new Intent(context, KeepAliveService.class));
        } catch (IllegalStateException | SecurityException e) {
            // A system reconnect can occur outside the allowed foreground-start window.
            // Keep the bound service alive, record the reason, and retry when the app is opened.
            Log.w("ClaudeAutoPass", "Foreground start deferred: " + e.getClass().getSimpleName());
            Control.prefs(context).edit().putBoolean("foreground_deferred", true).apply();
            Control.showFallback(context);
        }
    }
    @Override public void onCreate() { super.onCreate(); instance = this; }
    @Override public int onStartCommand(Intent intent, int flags, int startId) {
        if (!Control.running(this)) { stopSelf(); return START_NOT_STICKY; }
        show(); return START_STICKY;
    }
    private void show() {
        Notification notification = Control.notification(this);
        if (Build.VERSION.SDK_INT >= 34) startForeground(Control.NOTIFICATION, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE);
        else startForeground(Control.NOTIFICATION, notification);
        Control.prefs(this).edit().putBoolean("foreground_deferred", false).apply();
    }
    @Override public void onDestroy() {
        if (instance == this) instance = null;
        stopForeground(STOP_FOREGROUND_REMOVE);
        Log.i("ClaudeAutoPass", "Foreground service destroyed; running=" + Control.running(this));
        super.onDestroy();
    }
    @Override public IBinder onBind(Intent intent) { return null; }
}
