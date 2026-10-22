package com.personal.claudeautopass;

import android.app.DownloadManager;
import android.content.*;
import android.database.Cursor;
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
    static void download(Context c, Release release) throws IOException {
        File folder = new File(c.getExternalFilesDir(Environment.DIRECTORY_DOWNLOADS), "updates");
        if (!folder.isDirectory() && !folder.mkdirs()) throw new IOException("无法创建下载目录");
        // A unique file avoids overwriting an APK that a package installer may still be reading.
        String fileName = "update-" + System.currentTimeMillis() + ".apk";
        DownloadManager.Request request = new DownloadManager.Request(Uri.parse(release.url))
            .setTitle("Claude 自动审批 " + release.version).setDescription("正在下载更新")
            .setMimeType("application/vnd.android.package-archive")
            .setNotificationVisibility(DownloadManager.Request.VISIBILITY_VISIBLE_NOTIFY_COMPLETED)
            .setDestinationInExternalFilesDir(c, Environment.DIRECTORY_DOWNLOADS, "updates/" + fileName);
        long oldId = Control.prefs(c).getLong("download_id", -1);
        if (oldId != -1) c.getSystemService(DownloadManager.class).remove(oldId);
        long id = c.getSystemService(DownloadManager.class).enqueue(request);
        Control.prefs(c).edit().putLong("download_id", id).putString("download_file", fileName)
            .putString("download_version", release.version).putString("download_sha256", release.sha256).apply();
    }
    static final class Progress {
        int status, reason;
        long downloaded, total;
    }
    static Progress progress(Context c) {
        long id = Control.prefs(c).getLong("download_id", -1);
        if (id == -1) return null;
        try (Cursor cursor = c.getSystemService(DownloadManager.class).query(new DownloadManager.Query().setFilterById(id))) {
            if (cursor == null || !cursor.moveToFirst()) return null;
            Progress p = new Progress();
            p.status = cursor.getInt(cursor.getColumnIndexOrThrow(DownloadManager.COLUMN_STATUS));
            p.reason = cursor.getInt(cursor.getColumnIndexOrThrow(DownloadManager.COLUMN_REASON));
            p.downloaded = cursor.getLong(cursor.getColumnIndexOrThrow(DownloadManager.COLUMN_BYTES_DOWNLOADED_SO_FAR));
            p.total = cursor.getLong(cursor.getColumnIndexOrThrow(DownloadManager.COLUMN_TOTAL_SIZE_BYTES));
            return p;
        }
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
