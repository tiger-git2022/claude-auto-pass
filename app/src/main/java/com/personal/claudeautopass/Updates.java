package com.personal.claudeautopass;

import android.app.DownloadManager;
import android.content.*;
import android.net.Uri;
import android.os.Environment;
import org.json.*;
import java.io.*;
import java.net.*;
import java.util.Locale;

final class Updates {
    static final String REPO = "tiger-git2022/claude-auto-pass";
    static final String RELEASES = "https://github.com/" + REPO + "/releases";
    static final String APK_NAME = "claude-auto-pass.apk";
    static final class Release {
        final String version, notes, url, sha256;
        final long size;
        Release(String version, String notes, String url, String sha256, long size) {
            this.version = version; this.notes = notes; this.url = url; this.sha256 = sha256; this.size = size;
        }
    }
    static Release latest() throws Exception {
        HttpURLConnection connection = (HttpURLConnection) new URL("https://api.github.com/repos/" + REPO + "/releases/latest").openConnection();
        connection.setConnectTimeout(15000); connection.setReadTimeout(20000);
        connection.setRequestProperty("Accept", "application/vnd.github+json");
        connection.setRequestProperty("User-Agent", "ClaudeAutoPass-Android");
        try {
            int status = connection.getResponseCode();
            if (status == 404) throw new IOException("尚未发布正式版本");
            if (status == 403 || status == 429) throw new IOException("GitHub 请求次数受限，请稍后重试");
            if (status != 200) throw new IOException("GitHub 返回 HTTP " + status);
            ByteArrayOutputStream bytes = new ByteArrayOutputStream();
            try (InputStream in = connection.getInputStream()) {
                byte[] buffer = new byte[8192]; int count;
                while ((count = in.read(buffer)) != -1) {
                    if (bytes.size() + count > 2_000_000) throw new IOException("版本信息过大");
                    bytes.write(buffer, 0, count);
                }
            }
            JSONObject release = new JSONObject(bytes.toString("UTF-8"));
            String version = release.getString("tag_name");
            versionParts(version); // Require the same numeric version format used by the build.
            JSONArray assets = release.getJSONArray("assets");
            for (int i = 0; i < assets.length(); i++) {
                JSONObject asset = assets.getJSONObject(i);
                if (!APK_NAME.equals(asset.optString("name"))) continue;
                String url = asset.getString("browser_download_url");
                URI uri = new URI(url);
                if (!"https".equals(uri.getScheme()) || !"github.com".equals(uri.getHost()) || uri.getUserInfo() != null
                    || !uri.getPath().startsWith("/" + REPO + "/releases/download/")) throw new IOException("更新地址不属于项目仓库");
                String digest = asset.optString("digest", "");
                if (!digest.matches("sha256:[0-9a-fA-F]{64}")) digest = "";
                return new Release(version, release.optString("body", "暂无更新说明 / No release notes."), url, digest.replace("sha256:", "").toLowerCase(Locale.ROOT), asset.optLong("size", 0));
            }
            throw new IOException("此版本尚未提供 APK，请稍后重试");
        } finally { connection.disconnect(); }
    }
    static int[] versionParts(String version) { return Versioning.parts(version); }
    static boolean newer(String remote, String local) { return Versioning.newer(remote, local); }
    /**
     * Download state shared by every MainActivity instance, so leaving and reopening the app keeps showing it.
     * The APK is fetched by the app itself instead of the system DownloadManager: on some ROMs (seen on vivo
     * OriginOS) DownloadManager leaves GitHub downloads pending at 0 bytes forever.
     */
    static final class Progress {
        volatile boolean running;
        volatile long downloaded, total;
        volatile String error;
    }
    static final Progress PROGRESS = new Progress();
    private static final java.util.concurrent.ExecutorService DOWNLOADER = java.util.concurrent.Executors.newSingleThreadExecutor();

    /** Starts downloading in the background; returns false when a download is already running. */
    static synchronized boolean download(Context context, Release release) {
        if (PROGRESS.running) return false;
        Context c = context.getApplicationContext();
        PROGRESS.running = true; PROGRESS.downloaded = 0; PROGRESS.total = release.size; PROGRESS.error = null;
        forgetSystemDownload(c);
        Control.prefs(c).edit().remove("download_file").remove("download_done").apply();
        DOWNLOADER.execute(() -> {
            try {
                File folder = new File(c.getExternalFilesDir(Environment.DIRECTORY_DOWNLOADS), "updates");
                if (!folder.isDirectory() && !folder.mkdirs()) throw new IOException("无法创建下载目录");
                File[] old = folder.listFiles();
                if (old != null) for (File f : old) if (f.getName().endsWith(".part")) f.delete();
                // A unique file avoids overwriting an APK that a package installer may still be reading.
                String fileName = "update-" + System.currentTimeMillis() + ".apk";
                File part = new File(folder, fileName + ".part");
                fetch(release.url, part);
                if (release.size > 0 && part.length() != release.size) throw new IOException("下载不完整，请重试");
                if (!part.renameTo(new File(folder, fileName))) throw new IOException("无法保存更新文件");
                Control.prefs(c).edit().putString("download_file", fileName).putString("download_version", release.version)
                    .putString("download_sha256", release.sha256).putBoolean("download_done", true).apply();
            } catch (Exception e) {
                PROGRESS.error = e.getMessage() == null ? e.getClass().getSimpleName() : e.getMessage();
            } finally {
                PROGRESS.running = false;
            }
        });
        return true;
    }

    private static void fetch(String url, File target) throws IOException {
        HttpURLConnection connection = (HttpURLConnection) new URL(url).openConnection();
        connection.setConnectTimeout(20000); connection.setReadTimeout(30000);
        connection.setInstanceFollowRedirects(true); // GitHub redirects release assets to its download host (https).
        connection.setRequestProperty("User-Agent", "ClaudeAutoPass-Android");
        try {
            int status = connection.getResponseCode();
            if (status != 200) throw new IOException("下载返回 HTTP " + status);
            if (!"https".equals(connection.getURL().getProtocol())) throw new IOException("下载地址不是 HTTPS");
            long length = connection.getContentLengthLong();
            if (length > 0) PROGRESS.total = length;
            if (length > 100L * 1024 * 1024) throw new IOException("更新文件过大");
            try (InputStream in = connection.getInputStream(); OutputStream out = new FileOutputStream(target)) {
                byte[] buffer = new byte[65536]; int count; long done = 0;
                while ((count = in.read(buffer)) != -1) {
                    out.write(buffer, 0, count);
                    done += count;
                    PROGRESS.downloaded = done;
                    if (done > 100L * 1024 * 1024) throw new IOException("更新文件过大");
                }
            }
        } finally { connection.disconnect(); }
    }

    /** Drops a download that older versions queued in the system DownloadManager (it may be stuck). */
    static void forgetSystemDownload(Context c) {
        long id = Control.prefs(c).getLong("download_id", -1);
        if (id == -1) return;
        try { c.getSystemService(DownloadManager.class).remove(id); } catch (RuntimeException ignored) { }
        Control.prefs(c).edit().remove("download_id").apply();
    }

    /** True when a finished, not yet installed download exists. */
    static boolean downloaded(Context c) {
        if (!Control.prefs(c).getBoolean("download_done", false)) return false;
        try { return apk(c).isFile(); } catch (IOException e) { return false; }
    }

    static File apk(Context c) throws IOException {
        String name = Control.prefs(c).getString("download_file", "");
        if (!name.matches("update-[0-9]+\\.apk")) throw new IOException("无有效的更新文件");
        File folder = new File(c.getExternalFilesDir(Environment.DIRECTORY_DOWNLOADS), "updates").getCanonicalFile();
        File file = new File(folder, name).getCanonicalFile();
        if (!folder.equals(file.getParentFile()) || !file.isFile()) throw new IOException("找不到更新 APK");
        return file;
    }
    static void verify(Context c) throws Exception {
        File apk = apk(c);
        android.content.pm.PackageInfo archive = c.getPackageManager().getPackageArchiveInfo(apk.getAbsolutePath(), android.content.pm.PackageManager.GET_SIGNING_CERTIFICATES);
        android.content.pm.PackageInfo installed = c.getPackageManager().getPackageInfo(c.getPackageName(), android.content.pm.PackageManager.GET_SIGNING_CERTIFICATES);
        if (archive == null || !c.getPackageName().equals(archive.packageName) || archive.getLongVersionCode() <= installed.getLongVersionCode()) throw new IOException("APK 包名或版本校验失败");
        android.content.pm.Signature[] signatures = archive.signingInfo == null ? null : archive.signingInfo.getApkContentsSigners();
        android.content.pm.Signature[] current = installed.signingInfo == null ? null : installed.signingInfo.getApkContentsSigners();
        if (signatures == null || current == null || !new java.util.HashSet<>(java.util.Arrays.asList(signatures)).equals(new java.util.HashSet<>(java.util.Arrays.asList(current)))) throw new IOException("更新签名与当前版本不一致");
        String expected = Control.prefs(c).getString("download_sha256", "");
        if (!expected.isEmpty()) {
            java.security.MessageDigest digest = java.security.MessageDigest.getInstance("SHA-256");
            try (InputStream in = new FileInputStream(apk)) {
                byte[] buffer = new byte[65536]; int count;
                while ((count = in.read(buffer)) != -1) digest.update(buffer, 0, count);
            }
            StringBuilder actual = new StringBuilder();
            for (byte b : digest.digest()) actual.append(String.format(Locale.ROOT, "%02x", b & 255));
            if (!expected.equals(actual.toString())) throw new IOException("更新文件 SHA-256 校验失败，请重新下载");
        }
    }
}
