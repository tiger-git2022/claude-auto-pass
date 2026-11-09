plugins { id("com.android.application") }
// Raise together with CHANGELOG.md: a new feature raises the minor, a big change the major. CI passes the released
// version (app.version, the base or its next unused patch, see scripts/release_version.py).
val baseVersionName = "1.1.0"
val releaseVersion = providers.gradleProperty("app.version").orNull
val versionParts = (releaseVersion ?: baseVersionName).split('.').map(String::toInt)
require(versionParts.size == 3 && versionParts[0] < 100 && versionParts[1] < 1000 && versionParts[2] < 10000)
android {
    namespace = "com.personal.claudeautopass"
    compileSdk = 35
    defaultConfig {
        applicationId = "com.personal.claudeautopass"
        minSdk = 28
        targetSdk = 35
        // Above the earlier 1.0.0-tN builds (1000 + N). Local builds report the base version.
        versionCode = versionParts[0] * 10_000_000 + versionParts[1] * 10_000 + versionParts[2]
        versionName = releaseVersion ?: baseVersionName
    }
    signingConfigs {
        create("distribution") {
            val keyPath = System.getenv("APP_KEYSTORE_PATH")
            if (keyPath != null) {
                storeFile = file(keyPath)
                storePassword = System.getenv("APP_KEYSTORE_PASSWORD")
                keyAlias = "claude-auto-pass"
                keyPassword = System.getenv("APP_KEYSTORE_PASSWORD")
            }
            // minSdk 28 would make AGP sign with v2 only. Some installers (vivo OriginOS when opening a downloaded
            // APK) look for the classic JAR certificate in META-INF and reject the file without it, so sign all ways.
            enableV1Signing = true
            enableV2Signing = true
            enableV3Signing = true
        }
    }
    buildTypes { getByName("release") { signingConfig = signingConfigs.getByName("distribution") } }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
}

dependencies { implementation("com.google.android.material:material:1.14.0") }
