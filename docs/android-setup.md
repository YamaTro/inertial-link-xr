# Android sender setup

The reference Android implementation targets Android API 26+ and builds with
compile/target SDK 35, JDK 17, and Gradle. It is divided into:

- `:protocol-kotlin` — dependency-free ILXR codec and key handling;
- `:motion-source-android` — sensor source, mount transform, calibration, and
  bounded authenticated UDP sender; and
- `:sender-app` — minimal passenger-facing application.

## Build from source

Open the `android/` directory in Android Studio, allow the declared Android and
Kotlin plugins to resolve, and select `sender-app`. From a configured terminal:

```sh
cd android
./gradlew check assembleDebug assembleRelease
```

On Windows use `gradlew.bat`. `assembleDebug` creates a locally debug-signed APK
for controlled bench installation. `assembleRelease` is an unsigned verification
build and is not installable as a production release. Never distribute a debug
APK. Production signing, key custody, and store distribution are deliberately
out of scope until a separately reviewed maintainer process exists. Generated
APK/AAB files are not committed or attached to this Research Preview release.

## Permissions and privacy

The sender needs network access and, when continuous background sending is
implemented for a supported Android version, a visible foreground service. It
must not request camera, fine/coarse location, microphone, contacts, advertising
ID, account, or broad storage permission. There is no telemetry or discovery.

Review the merged app manifest before every release:

```sh
./gradlew :sender-app:processReleaseMainManifest
```

Treat any newly merged sensitive permission as a release blocker until publicly
reviewed and documented.

The v0.1 reference sender targets SDK 35, so `INTERNET` is the correct and only
network permission. An integrating Android host that later targets Android 17
(SDK 37+) must follow Android's
[local network permission guidance](https://developer.android.com/privacy-and-security/local-network-permission):
declare and request `ACCESS_LOCAL_NETWORK` at runtime before LAN UDP, and stop
network motion/fade neutral if it is denied or revoked. Apps targeting SDK 36 or
lower must not add or request that permission early; `INTERNET` grants implicit
LAN access on those targets.

## Pair and send

1. Keep the vehicle/headset stationary and use a private local link.
2. In the Unity application, listen on UDP `28461` and provide a runtime pairing
   entry UI. Do not serialize a key in a scene.
3. In the Android sender, enter the receiver's explicit private IP and port.
   No broadcast discovery is performed.
4. Start a new sender session. Transfer its displayed 32-hex-character pairing
   code to Unity. The key and random session ID are regenerated for every sender
   session, including after a failed start or return from background.
5. Rigidly mount the phone screen-up/top-forward or select a reviewed custom
   mount transform.
6. While fully stationary, request calibration and wait for completion.
7. Confirm sensor validity, packet counters, sync, age, loss, and Unity safety
   state before enabling any content cue.

The app's sensor path prefers `TYPE_GAME_ROTATION_VECTOR`, avoiding an absolute
heading claim, and falls back to `TYPE_ROTATION_VECTOR`. Accelerometer and
gyroscope are required. Gravity, linear acceleration, and rotation availability
are carried in status bits; consumers do not assume every phone supplies the
same fused sensors.

## Embedding the libraries

Apps can use the libraries without the reference UI. Construct
`AndroidMotionSource` with an application context and explicit
`MountTransform`, create an ephemeral `PairingKey`, then pass both with a
validated `UdpEndpoint` into `AuthenticatedUdpMotionSender`. The sender clones
the key and zeroes its private copy in `close()`; the caller still owns the
original `PairingKey` and MUST call its `destroy()`/`close()` in a `finally`
block. Close the sender and motion source from the same visible lifecycle.

`AuthenticatedUdpMotionSender` is single-start/single-session. After `close()`,
do not restart it: construct a new sender with a new session ID and a newly
generated `PairingKey`. A failed start or background transition follows the
same destroy-and-recreate rule.

Do not accept receiver addresses from untrusted deep links, logs, QR codes, or
remote configuration without a separate threat-model review. Do not persist
the key merely for convenience.

Complete [calibration](calibration.md) and [safety checks](safety.md) before use.
