plugins {
    id("com.android.library")
    kotlin("android")
    `maven-publish`
}

val generatedLegalResources = layout.buildDirectory.dir("generated/legal-resources")
val generateLegalResources by tasks.registering(Copy::class) {
    from(rootProject.file("../LICENSE"), rootProject.file("../NOTICE"))
    into(generatedLegalResources.map { it.dir("META-INF") })
}

android {
    namespace = "io.github.yamatro.inertiallink.motion"
    compileSdk = 35

    defaultConfig {
        minSdk = 26
        consumerProguardFiles("consumer-rules.pro")
        testInstrumentationRunner = "android.test.InstrumentationTestRunner"
    }

    buildFeatures {
        buildConfig = false
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    kotlinOptions {
        jvmTarget = "17"
    }

    testOptions {
        unitTests.all {
            it.useJUnitPlatform()
        }
    }

    publishing {
        singleVariant("release") {
            withSourcesJar()
        }
    }

    sourceSets.named("main") {
        resources.srcDir(generatedLegalResources)
    }
}

tasks.named("preBuild") {
    dependsOn(generateLegalResources)
}

dependencies {
    api(project(":protocol-kotlin"))
    testImplementation(kotlin("test"))
}

afterEvaluate {
    publishing {
        publications {
            create<MavenPublication>("release") {
                artifactId = "inertial-link-motion-android"
                from(components["release"])
                pom {
                    name.set("InertialLink XR Android Motion Source")
                    description.set("SensorManager motion source and authenticated UDP sender for InertialLink XR")
                    url.set("https://github.com/YamaTro/inertial-link-xr")
                    licenses {
                        license {
                            name.set("Apache License, Version 2.0")
                            url.set("https://www.apache.org/licenses/LICENSE-2.0.txt")
                        }
                    }
                }
            }
        }
    }
}
