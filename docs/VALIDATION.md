# Validation record for v0.2.0

Date: 2026-07-22
Status: Research Preview

This file separates checks that actually ran from checks that remain unavailable.
It is not a safety, medical, clinical, or compatibility certification.

## Verified locally during release preparation

| Area | Command / evidence | Result |
| --- | --- | --- |
| Node protocol and security boundaries | `corepack pnpm check` on Node 24.13.1 | 21/21 deterministic unit/mutation tests pass, including the pinned-endpoint UDP relay boundary; 1 recording with 5 synthetic samples validates; 149 local Markdown links across 44 files resolve. |
| Node loopback transport | Inspector and connected synthetic sender with the documented public test key | The inspector authenticated and accepted 7 sequential synthetic IMU datagrams on `127.0.0.1`; no non-loopback interface or real sensor was used. |
| C# protocol core | `dotnet run --project tests/dotnet/InertialLink.Core.Tests.csproj --configuration Release` | 24/24 dependency-free protocol, replay, timing, filtering, and safety-gate executable checks pass. |
| Android source | From `android/`: Gradle 8.11.1 with `--offline --no-daemon --dependency-verification strict check assembleDebug assembleRelease` | `BUILD SUCCESSFUL`; protocol and motion tests pass and release lint reports no errors. `aapt2` reports `INTERNET` only for release; the local debug APK additionally requests `WAKE_LOCK` for the five-minute-capped locked-device validation path. |
| Unity-facing source compilation | `dotnet build tests/dotnet/InertialLink.Unity.Compile.csproj --configuration Release --property:UnityEditorManagedDirectory=...` against installed Unity 6000.1.14f1 assemblies | Build succeeds with 0 warnings and 0 errors. This is a static compatibility check, not an Editor or headset run. |
| Unity package Edit Mode tests | Unity 6000.1.14f1 Test Runner on 2026-07-21 | 19/19 tests pass, failures/skips 0. Coverage includes Camera/XR Origin hierarchy refusal, invalid source/number handling, neutral failover, acceleration-alignment bounds, protected-center and curved-grid geometry, bounded flow, and late hierarchy mutation. |
| Unity vertical-video Play Mode demo | Normal Unity 6000.1.14f1 Editor Play Mode using the local validation harness and generated 9:16 H.264 asset | Video prepared and played at 720x1280; frame 30 was captured; 72 cue particles remained outside the protected central half-width; Camera pose stayed unchanged. At capture, synthetic measured X acceleration was 1.134 m/s², the deliberately under-responsive virtual value was 0.816 m/s², mismatch was 0.318 m/s², and the bounded suggested correction was 0.159 m/s². |
| Xiaomi 13T to Unity integration | Xiaomi XIG04, Android 15/API 35, Unity 6000.1.14f1 on 2026-07-22 | 216 authenticated packets accepted, 0 rejected/dropped, clock synchronized at 5.448 ms best RTT, safety weight 1.0, local 9:16 video playing at frame 112, 2,019 directional cues active, and Camera pose unchanged. No pairing key was written to evidence. A pinned, bounded local relay handled the existing Windows Public-profile Unity block without changing firewall configuration. |
| Shared wire vectors | Kotlin, C#, and Node test suites consume `protocol/TEST_VECTORS.md` | Authenticated IMU/sync bytes and failure-path rules are cross-checked in all three implementations. |
| Release automation supply chain | Official Git tag refs were queried read-only on 2026-07-17 | Every third-party action is pinned to the full official tag SHA listed below. |

The tagged GitHub Actions run is the authoritative hosted record. Its read-only
`verify` job repeats Node, .NET core, and Android checks, creates the Unity
tarball/Kotlin JAR/Android AAR/specification/license assets and checksums, then
uploads one handoff artifact. Only the dependent `publish` job receives
`contents: write`; it checks that the tag still targets the verified commit and
creates a prerelease from the handoff.

## Not verified in v0.2.0

- No Meta Quest/other OpenXR headset, bus/car cabin, human motion trial, or
  long-duration thermal/background run was available for release qualification.
- No human-subject, usability, accessibility, motion-sickness efficacy, medical,
  or clinical trial has been performed. The cue cannot be described as preventing
  or reducing sickness for an individual or population.
- No independent security audit or exhaustive fuzzer has been completed. The
  repository includes deterministic bounded parser mutations and explicit tests
  for authentication, malformed input, replay, cross-type sequence reuse,
  staleness, numeric bounds, and safe fade-out; these do not prove absence of bugs.
- The project is an opt-in Unity application integration. It is not a Quest or
  OpenXR system overlay and has not been tested inside arbitrary third-party apps.
- Unity's Windows VideoPlayer did not prepare the MP4 in Editor batch mode. The
  same local asset prepared and played in normal Editor Play Mode, so automated
  video validation currently requires a graphical Editor session.

## Research-evidence boundary

The Play Mode demo validates implementation mechanics, not human efficacy. Prior
peer-reviewed vehicle studies report reduced sickness from synchronized
background/peripheral visual motion, including on-road video viewing, but no
participant in this project has been exposed to the cue. See
[Research basis and claim boundary](research-basis.md).

The reviewed real-device screenshot is stored at
[`assets/xiaomi13t-unity-demo.png`](assets/xiaomi13t-unity-demo.png). It contains
only the Unity presentation surface and summary diagnostics; the ignored local
JSON evidence, raw validation logs, pairing key, and device-control artifacts are
not published.

## Release artifact boundary

The Research Preview release contains source archives, a Unity Package Manager
tarball, Kotlin protocol JAR, Android motion-source AAR, ILXR/1.0 specification,
license/notices, and `SHA256SUMS`. It intentionally excludes the unsigned
release APK, debug-signed APK, keystore/signing key, pairing key, packet capture,
real-trip recording, and human-subject data.

The generated vertical-video frame, MP4, screenshot, logs, and JSON evidence are
local presentation/validation outputs. They are not part of the source release or
the Android/Unity package artifacts.

## Pinned GitHub Actions provenance

The following values were compared with each action's official GitHub tag using
`git ls-remote` on 2026-07-17:

| Action tag | Full pinned SHA |
| --- | --- |
| `actions/checkout@v7.0.0` | `9c091bb21b7c1c1d1991bb908d89e4e9dddfe3e0` |
| `actions/setup-node@v7.0.0` | `820762786026740c76f36085b0efc47a31fe5020` |
| `actions/setup-java@v5.6.0` | `03ad4de0992f5dab5e18fcb136590ce7c4a0ac95` |
| `gradle/actions@v6.2.0` | `3f131e8634966bd73d06cc69884922b02e6faf92` |
| `actions/upload-artifact@v7.0.1` | `043fb46d1a93c77aae656e7c1c64a875d1fc6a0a` |

Re-check tag ownership and review upstream changes before updating a pin;
Dependabot output is a proposal, not automatic trust.
