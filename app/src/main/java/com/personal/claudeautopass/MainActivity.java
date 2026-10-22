package com.personal.claudeautopass;

import android.Manifest;
import android.app.*;
import android.content.*;
import android.content.pm.PackageManager;
import android.graphics.Color;
import android.net.Uri;
import android.os.*;
import android.provider.Settings;
import android.view.*;
import android.widget.*;
import java.util.Locale;
import java.util.concurrent.*;

public final class MainActivity extends androidx.appcompat.app.AppCompatActivity {
    private final Handler handler = new Handler(Looper.getMainLooper());
    private final ExecutorService worker = Executors.newSingleThreadExecutor();
    private TextView status, permissionStatus, lastAction, updateStatus;
    private Button toggle, notificationButton, accessibilityButton, installButton, checkButton;
    private ProgressBar progress;
    private boolean visible;
    private boolean verifying;
    private String version = "1.0.0";
    private final Runnable refreshLoop = new Runnable() {
        @Override public void run() { if (!visible) return; refresh(); handler.postDelayed(this, 700); }
    };
    private int dp(int value) { return Math.round(value * getResources().getDisplayMetrics().density); }
    @Override public void onCreate(Bundle saved) {
        com.google.android.material.color.DynamicColors.applyToActivityIfAvailable(this);
        super.onCreate(saved);
        try { version = getPackageManager().getPackageInfo(getPackageName(), 0).versionName; } catch (Exception ignored) { }
        ScrollView scroll = new ScrollView(this);
        LinearLayout body = new LinearLayout(this); body.setOrientation(LinearLayout.VERTICAL);
        body.setPadding(dp(24), dp(24), dp(24), dp(24)); body.setBackgroundColor(com.google.android.material.color.MaterialColors.getColor(body, com.google.android.material.R.attr.colorSurface));
        scroll.addView(body); setContentView(scroll);
        if (Build.VERSION.SDK_INT >= 35) {
            scroll.setOnApplyWindowInsetsListener((view, insets) -> {
                android.graphics.Insets bars = insets.getInsets(WindowInsets.Type.systemBars());
                view.setPadding(bars.left, bars.top, bars.right, bars.bottom); return insets;
            });
        }
        TextView title = text(body, "Claude 自动审批", 25); title.setTextColor(com.google.android.material.color.MaterialColors.getColor(title, androidx.appcompat.R.attr.colorPrimary));
        text(body, "优先“本次会话中始终允许”，其次“允许一次”。", 15);
        status = text(body, "", 20);
        permissionStatus = text(body, "", 14);
        accessibilityButton = button(body, "开启无障碍服务", () -> startActivity(new Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS)));
        button(body, "后台运行设置", () -> new com.google.android.material.dialog.MaterialAlertDialogBuilder(this)
            .setTitle("保持后台运行").setMessage("如果手机仍会断开服务，请在应用详情的电池或耗电管理中允许后台运行，并开启自启动；也可在最近任务中锁定本 App。系统入口因机型而异。")
            .setNegativeButton("取消", null).setPositiveButton("打开应用详情", (dialog, which) -> startActivity(new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS, Uri.parse("package:" + getPackageName())))).show());
        notificationButton = button(body, "允许通知（通知栏开关）", this::requestNotifications);
        toggle = button(body, "开启自动审批", () -> {
            if (!AccessibilityState.granted(this)) {
                toast("请先开启 Claude 自动审批的无障碍服务");
                startActivity(new Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS)); return;
            }
            if (!Control.enabled(this) && !notificationsAllowed()) { requestNotifications(); return; }
            Control.setEnabled(this, !Control.enabled(this));
            if (Control.enabled(this) && !ApprovalService.connected()) toast("已开启，等待系统重连后自动恢复");
            refresh();
        });
        button(body, "退出（保留无障碍权限）", () -> { Control.exit(this); finishAndRemoveTask(); });
        lastAction = text(body, "", 13);
        text(body, "首次安装默认关闭。系统重连保留你的开关状态。退出停止审批和前台保活、移除通知，无障碍权限保持开启。", 13);
        text(body, "版本 " + version, 15);
        checkButton = button(body, "检查更新", this::checkUpdates);
        updateStatus = text(body, "", 14);
        progress = new com.google.android.material.progressindicator.LinearProgressIndicator(this);
        progress.setMax(100); progress.setVisibility(View.GONE); body.addView(progress);
        installButton = button(body, "安装已下载的更新", this::installUpdate); installButton.setVisibility(View.GONE);
        button(body, "打开 GitHub 仓库", () -> startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse("https://github.com/" + Updates.REPO))));
        text(body, "无障碍会读取 Claude 当前界面的按钮文字并代你批准操作。只在主动开启后运行。联网仅用于检查和下载 GitHub 更新，不上传对话。", 13);
    }
    private TextView text(LinearLayout parent, String value, int size) {
        TextView view = new com.google.android.material.textview.MaterialTextView(this); view.setText(value); view.setTextSize(size);
        view.setTextColor(com.google.android.material.color.MaterialColors.getColor(view, com.google.android.material.R.attr.colorOnSurface)); view.setPadding(0, dp(8), 0, dp(8)); parent.addView(view); return view;
    }
    private Button button(LinearLayout parent, String label, Runnable action) {
        com.google.android.material.button.MaterialButton button = new com.google.android.material.button.MaterialButton(this); button.setText(label); button.setAllCaps(false);
        button.setOnClickListener(view -> action.run()); parent.addView(button, new LinearLayout.LayoutParams(-1, -2)); return button;
    }
    private void toast(String text) { Toast.makeText(this, text, Toast.LENGTH_LONG).show(); }
    private boolean notificationsAllowed() { return getSystemService(NotificationManager.class).areNotificationsEnabled(); }
    private void requestNotifications() {
        if (Build.VERSION.SDK_INT >= 33 && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED
            && !Control.prefs(this).getBoolean("notification_requested", false)) {
            Control.prefs(this).edit().putBoolean("notification_requested", true).apply();
            requestPermissions(new String[]{Manifest.permission.POST_NOTIFICATIONS}, 1);
        } else startActivity(new Intent(Settings.ACTION_APP_NOTIFICATION_SETTINGS).putExtra(Settings.EXTRA_APP_PACKAGE, getPackageName()));
    }
    @Override protected void onResume() {
        super.onResume(); visible = true; Control.open(this); handler.removeCallbacks(refreshLoop); handler.post(refreshLoop);
    }
    @Override protected void onPause() { visible = false; handler.removeCallbacks(refreshLoop); super.onPause(); }
    @Override protected void onDestroy() { handler.removeCallbacksAndMessages(null); worker.shutdownNow(); super.onDestroy(); }
    @Override public void onRequestPermissionsResult(int code, String[] permissions, int[] results) {
        super.onRequestPermissionsResult(code, permissions, results); Control.refresh(this); refresh();
    }
    private void refresh() {
        boolean connected = ApprovalService.connected(), granted = AccessibilityState.granted(this), enabled = Control.enabled(this), notify = notificationsAllowed();
        status.setText(!granted ? "尚未开启无障碍权限" : !connected ? "无障碍权限已开，等待系统连接" : enabled ? "自动审批已开启" : "自动审批已关闭");
        permissionStatus.setText("无障碍：" + (granted ? connected ? "已连接" : "已授权，等待连接" : "未授权") + "    通知：" + (notify ? "已允许" : "未允许"));
        accessibilityButton.setText(granted ? "无障碍设置（已授权）" : "开启无障碍服务");
        notificationButton.setVisibility(notify ? View.GONE : View.VISIBLE);
        toggle.setText(enabled ? "关闭自动审批" : "开启自动审批");
        String action = Control.prefs(this).getString("last_action", "尚未点击审批");
        long time = Control.prefs(this).getLong("last_action_time", 0);
        lastAction.setText(time == 0 ? action : "上次操作：" + action + "  " + android.text.format.DateFormat.format("MM-dd HH:mm:ss", time));
        refreshDownload();
    }
    private void checkUpdates() {
        checkButton.setEnabled(false); updateStatus.setText("正在检查 GitHub 正式版本…");
        worker.execute(() -> {
            try {
                Updates.Release release = Updates.latest();
                boolean newer = Updates.newer(release.version, version);
                runOnUiThread(() -> {
                    if (isFinishing() || isDestroyed()) return;
                    checkButton.setEnabled(true);
                    updateStatus.setText(newer ? "发现新版本 " + release.version : "当前已是最新版本 " + version);
                    if (newer) new com.google.android.material.dialog.MaterialAlertDialogBuilder(this).setTitle("新版本 " + release.version)
                        .setMessage(release.notes).setNegativeButton("稍后", null)
                        .setPositiveButton("下载更新", (dialog, which) -> {
                            try { Updates.download(this, release); refreshDownload(); }
                            catch (Exception e) { updateStatus.setText("下载失败：" + e.getMessage()); }
                        }).show();
                });
            } catch (Exception e) {
                runOnUiThread(() -> { if (!isDestroyed()) { checkButton.setEnabled(true); updateStatus.setText("检查失败：" + e.getMessage()); } });
            }
        });
    }
    private void refreshDownload() {
        if (verifying || !checkButton.isEnabled()) return;
        String downloadedVersion = Control.prefs(this).getString("download_version", "");
        if (!downloadedVersion.isEmpty() && !Updates.newer(downloadedVersion, version)) {
            Control.prefs(this).edit().remove("download_id").apply();
        }
        Updates.Progress p = Updates.progress(this);
        if (p == null) { progress.setVisibility(View.GONE); installButton.setVisibility(View.GONE); return; }
        boolean done = p.status == DownloadManager.STATUS_SUCCESSFUL;
        installButton.setVisibility(done ? View.VISIBLE : View.GONE);
        if (done) { progress.setVisibility(View.GONE); updateStatus.setText("更新下载完成，点击安装"); return; }
        if (p.status == DownloadManager.STATUS_FAILED) {
            progress.setVisibility(View.GONE); updateStatus.setText("更新下载失败（" + p.reason + "），可重新检查并下载"); return;
        }
        progress.setVisibility(View.VISIBLE); progress.setIndeterminate(p.total <= 0);
        int percent = p.total <= 0 ? 0 : (int) Math.min(100, p.downloaded * 100 / p.total);
        if (p.total > 0) progress.setProgress(percent);
        updateStatus.setText((p.status == DownloadManager.STATUS_PAUSED ? "等待网络 · " : "下载更新 · ")
            + (p.total <= 0 ? String.format(Locale.ROOT, "%.1f MB", p.downloaded / 1048576.0) : percent + "%"));
    }
    private void installUpdate() {
        if (verifying) return;
        if (!getPackageManager().canRequestPackageInstalls()) {
            new com.google.android.material.dialog.MaterialAlertDialogBuilder(this).setTitle("允许安装更新")
                .setMessage("请在系统设置中允许此 App 安装应用，然后返回点击安装。")
                .setNegativeButton("取消", null).setPositiveButton("去设置", (dialog, which) ->
                    startActivity(new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:" + getPackageName())))).show(); return;
        }
        verifying = true; installButton.setEnabled(false); updateStatus.setText("正在校验版本、签名和下载文件…");
        worker.execute(() -> {
            try {
                Updates.verify(this);
                runOnUiThread(() -> {
                    verifying = false; installButton.setEnabled(true);
                    if (isFinishing() || isDestroyed()) return;
                    Uri uri = Uri.parse("content://" + getPackageName() + ".apk/update.apk");
                    Intent install = new Intent(Intent.ACTION_VIEW).setDataAndType(uri, "application/vnd.android.package-archive")
                        .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
                    install.setClipData(ClipData.newRawUri("update", uri));
                    try { startActivity(install); } catch (ActivityNotFoundException e) { toast("系统未提供 APK 安装器"); }
                });
            } catch (Exception e) {
                runOnUiThread(() -> { verifying = false; installButton.setEnabled(true); toast("校验失败：" + e.getMessage()); });
            }
        });
    }
}
