plugins { id("com.android.application") }
val baseVersionName = "1.0.0"
val baseVersionCode = 1
val buildNumber = providers.gradleProperty("app.buildNumber").orElse("0").get().toInt()
require(buildNumber in 0..999)
android {
    namespace = "com.personal.claudeautopass"
    compileSdk = 35
    defaultConfig {
        applicationId = "com.personal.claudeautopass"
        minSdk = 28
        targetSdk = 35
        versionCode = baseVersionCode * 1000 + buildNumber
        versionName = baseVersionName + if (buildNumber > 0) "-t$buildNumber" else ""
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
        }
    }
    buildTypes { getByName("release") { signingConfig = signingConfigs.getByName("distribution") } }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
}

dependencies { implementation("com.google.android.material:material:1.14.0") }
