plugins {
    kotlin("jvm")
    `java-library`
    `maven-publish`
}

val generatedLegalResources = layout.buildDirectory.dir("generated/legal-resources")
val generateLegalResources by tasks.registering(Copy::class) {
    from(rootProject.file("../LICENSE"), rootProject.file("../NOTICE"))
    into(generatedLegalResources.map { it.dir("META-INF") })
}

java {
    sourceCompatibility = JavaVersion.VERSION_17
    targetCompatibility = JavaVersion.VERSION_17
    withSourcesJar()
}

sourceSets.main {
    resources.srcDir(generatedLegalResources)
}

kotlin {
    compilerOptions {
        jvmTarget.set(org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17)
    }
}

dependencies {
    testImplementation(kotlin("test"))
}

tasks.test {
    useJUnitPlatform()
}

tasks.processResources {
    dependsOn(generateLegalResources)
}

publishing {
    publications {
        create<MavenPublication>("protocol") {
            artifactId = "inertial-link-protocol-kotlin"
            from(components["java"])
            pom {
                name.set("InertialLink XR Kotlin Protocol")
                description.set("Bounded authenticated InertialLink XR wire codec")
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
