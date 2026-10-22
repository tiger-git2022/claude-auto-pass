package com.personal.claudeautopass;
public final class VersioningTest {
    public static void main(String[] args) {
        String[][] cases = {{"v1.0.0-t2","1.0.0-t1","true"},{"v1.0.0-t10","1.0.0-t9","true"},
            {"v1.0.0-t1","1.0.0-t1","false"},{"v1.0.0-t1","1.0.0-t2","false"},
            {"v1.0.0-t1","1.0.0","true"},{"v1.1.0-t1","1.0.0-t999","true"},
            {"v1.0.1-t1","1.0.0-t100","true"},{"v1.0.0","1.0.0-t1","false"}};
        for (String[] c : cases) if (Versioning.newer(c[0], c[1]) != Boolean.parseBoolean(c[2])) throw new AssertionError(c[0] + " vs " + c[1]);
        for (String invalid : new String[]{"v1.0.0-evil", "1.0.0.1", "latest", "v999999999999.0.0"}) {
            try { Versioning.parts(invalid); throw new AssertionError("Accepted " + invalid); } catch (IllegalArgumentException expected) { }
        }
        System.out.println("Passed 12 update version checks.");
    }
}
