plugins {
    id("com.android.application")
    // The Flutter Gradle Plugin must be applied after the Android and Kotlin Gradle plugins.
    id("dev.flutter.flutter-gradle-plugin")
}

android {
    namespace = "com.decoded.furbrains_host"
    compileSdk = flutter.compileSdkVersion
    // Use the same NDK as Unity 6000.3 (r27c). It ships inside the Unity install, so nothing needs downloading.
    ndkVersion = "27.2.12479018"
    ndkPath = "C:/Program Files/Unity/Hub/Editor/6000.3.28f1/Editor/Data/PlaybackEngines/AndroidPlayer/NDK"

    // Unity's data files are already compressed by Unity; compressing them again makes the game load slower.
    androidResources {
        val unityStreamingAssetsList = (project.findProperty("unityStreamingAssets") as? String)
            ?.split(",")
            ?.map { it.trim() }
            ?.filter { it.isNotEmpty() }
            ?: emptyList()
        noCompress += listOf(".unity3d", ".ress", ".resource", ".obb", ".bundle", ".unityexp") +
            unityStreamingAssetsList
        ignoreAssetsPattern = "!.svn:!.git:!.ds_store:!*.scc:!CVS:!thumbs.db:!picasa.ini:!*~"
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    defaultConfig {
        // TODO: Specify your own unique Application ID (https://developer.android.com/studio/build/application-id.html).
        applicationId = "com.decoded.furbrains_host"
        // You can update the following values to match your application needs.
        // For more information, see: https://flutter.dev/to/review-gradle-config.
        // At least the Unity project's Minimum API Level (Player Settings: 26).
        minSdk = 26
        targetSdk = flutter.targetSdkVersion
        // Uses the version code from pubspec.yaml. When using split APKs, 1000 * ABI_VERSION
        // is added automatically by Flutter. (https://developer.android.com/studio/build/configure-apk-splits#configure-APK-versions)
        // You can force using the value of versionCode by specifying the `-P force-version-code-ignoring-abi=true`
        // flag during build.
        versionCode = flutter.versionCode
        versionName = flutter.versionName
    }

    buildTypes {
        release {
            // TODO: Add your own signing config for the release build.
            // Signing with the debug keys for now, so `flutter run --release` works.
            signingConfig = signingConfigs.getByName("debug")
        }
    }
}

kotlin {
    compilerOptions {
        jvmTarget = org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17
    }
}

flutter {
    source = "../.."
}

dependencies {
    // The exported Unity game (see settings.gradle.kts).
    implementation(project(":unityLibrary"))
}
