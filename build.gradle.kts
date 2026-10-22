plugins { id("com.android.application") version "9.4.1" apply false }
// Keep generated files out of OneDrive locally; CI uses normal in-workspace outputs.
if (System.getenv("CI") == null) {
    val localBuildRoot = file(System.getProperty("user.home")).resolve(".gradle/project-builds/claude-auto-pass")
    layout.buildDirectory.set(localBuildRoot.resolve("root"))
    subprojects { layout.buildDirectory.set(localBuildRoot.resolve(name)) }
}
