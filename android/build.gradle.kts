buildscript {
    repositories {
        google()
        mavenCentral()
    }
    dependencies {
        classpath("com.android.tools.build:gradle:8.7.2")
        classpath("org.jetbrains.kotlin:kotlin-gradle-plugin:2.4.10")
    }
}

allprojects {
    group = "io.github.yamatro.inertiallink"
    version = "0.2.0"
}
