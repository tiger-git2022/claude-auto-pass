package com.personal.claudeautopass;
final class Versioning {
    static int[] parts(String version) {
        if (!version.matches("v?\\d{1,6}\\.\\d{1,6}\\.\\d{1,6}(?:-t\\d{1,6})?")) throw new IllegalArgumentException("版本号格式不受支持");
        String[] values = version.replaceFirst("^v", "").replace("-t", ".").split("\\.");
        return new int[] {Integer.parseInt(values[0]), Integer.parseInt(values[1]), Integer.parseInt(values[2]), values.length == 4 ? Integer.parseInt(values[3]) : 0};
    }
    static boolean newer(String remote, String local) {
        int[] r = parts(remote), l = parts(local);
        for (int i = 0; i < r.length; i++) if (r[i] != l[i]) return r[i] > l[i];
        return false;
    }
}
