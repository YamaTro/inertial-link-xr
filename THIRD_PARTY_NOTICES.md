# Third-party notices

This inventory covers material stored in the source repository. Build tools may
resolve additional dependencies whose own metadata and licenses remain
authoritative.

## Gradle Wrapper 8.11.1

The repository includes Gradle Wrapper launch scripts and
`android/gradle/wrapper/gradle-wrapper.jar` from the Gradle 8.11.1 project.

- Project and source: <https://github.com/gradle/gradle/tree/v8.11.1>
- License: Apache License 2.0
- License text: <https://github.com/gradle/gradle/blob/v8.11.1/LICENSE>
- Official checksum reference: <https://gradle.org/release-checksums/>

The wrapper downloads `gradle-8.11.1-bin.zip`; that distribution is not stored
in this repository. Its official SHA-256 is pinned in
`android/gradle/wrapper/gradle-wrapper.properties`. The bundled wrapper JAR was
verified against the official Gradle 8.11.1 wrapper-JAR checksum.

## Resolved build dependencies

Android Gradle Plugin, Kotlin tooling, Android SDK components, Unity, and .NET
are resolved or installed by their respective toolchains and are not vendored
as runtime source in this repository. Consult the resolved dependency metadata
and tool installations for their licenses before redistribution. Generated
build directories and downloaded caches are excluded from version control.

The `tools/` Node.js utilities have no third-party package dependencies.
