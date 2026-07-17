plugins {
    id("com.android.application")
    kotlin("android")
}

android {
    namespace = "io.github.yamatro.inertiallink.sender"
    compileSdk = 35

    defaultConfig {
        applicationId = "io.github.yamatro.inertiallink.sender"
        minSdk = 26
        targetSdk = 35
        versionCode = 1
        versionName = "0.1.0"
    }

    buildTypes {
        release {
            isMinifyEnabled = true
            proguardFiles(
                getDefaultProguardFile("proguard-android-optimize.txt"),
                "proguard-rules.pro",
            )
        }
    }

    buildFeatures {
        buildConfig = true
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    kotlinOptions {
        jvmTarget = "17"
    }
}

dependencies {
    implementation(project(":motion-source-android"))
    implementation(project(":protocol-kotlin"))
}
