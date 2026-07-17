# Validation record for v0.1.0

Date: 2026-07-17
Status: Research Preview

This file separates checks that actually ran from checks that remain unavailable.
It is not a safety, medical, clinical, or compatibility certification.

## Verified locally during release preparation

| Area | Command / evidence | Result |
| --- | --- | --- |
| Node protocol and security boundaries | `npm run check` on Node 24.13.1 | 20/20 deterministic unit/mutation tests pass; 1 recording with 5 synthetic samples validates; 88 local Markdown links across 38 files resolve. |
| Node loopback transport | Inspector and connected synthetic sender with the documented public test key | The inspector authenticated and accepted 7 sequential synthetic IMU datagrams on `127.0.0.1`; no non-loopback interface or real sensor was used. |
| C# protocol core | `dotnet run --project tests/dotnet/InertialLink.Core.Tests.csproj --configuration Release` | 24/24 dependency-free protocol, replay, timing, filtering, and safety-gate executable checks pass. |
| Android source | From `android/`: Gradle 8.11.1 with `--offline --no-daemon --dependency-verification strict check assembleDebug assembleRelease` | `BUILD SUCCESSFUL`; 189 tasks, 15 protocol tests and 21 motion tests for each debug/release variant, failures/errors/skips 0, lint errors 0. The only lint warning is the documented SDK 35 `OldTargetApi`; `aapt2` reports only `INTERNET`. |
| Unity-facing source compilation | `dotnet build tests/dotnet/InertialLink.Unity.Compile.csproj --configuration Release --property:UnityEditorManagedDirectory=...` against installed Unity 6000.1.14f1 assemblies | Build succeeds with 0 warnings and 0 errors. This is a static compatibility check, not an Editor or headset run. |
| Shared wire vectors | Kotlin, C#, and Node test suites consume `protocol/TEST_VECTORS.md` | Authenticated IMU/sync bytes and failure-path rules are cross-checked in all three implementations. |
| Release automation supply chain | Official Git tag refs were queried read-only on 2026-07-17 | Every third-party action is pinned to the full official tag SHA listed below. |

The tagged GitHub Actions run is the authoritative hosted record. Its read-only
`verify` job repeats Node, .NET core, and Android checks, creates the Unity
tarball/Kotlin JAR/Android AAR/specification/license assets and checksums, then
uploads one handoff artifact. Only the dependent `publish` job receives
`contents: write`; it checks that the tag still targets the verified commit and
creates a prerelease from the handoff.

## Not verified in v0.1.0

- Unity Edit Mode and Play Mode tests were attempted with Unity 6000.1.14f1 on
  2026-07-16, but the Editor exited before running tests because no valid
  `com.unity.editor.headless` entitlement/license was available. No Unity test
  pass is claimed.
- No physical Android phone, Meta Quest/other OpenXR headset, USB/Wi-Fi transport,
  Android emulator, bus/car cabin, or long-duration thermal/background run was
  available for release qualification.
- No human-subject, usability, accessibility, motion-sickness efficacy, medical,
  or clinical trial has been performed. The cue cannot be described as preventing
  or reducing sickness for an individual or population.
- No independent security audit or exhaustive fuzzer has been completed. The
  repository includes deterministic bounded parser mutations and explicit tests
  for authentication, malformed input, replay, cross-type sequence reuse,
  staleness, numeric bounds, and safe fade-out; these do not prove absence of bugs.
- The project is an opt-in Unity application integration. It is not a Quest or
  OpenXR system overlay and has not been tested inside arbitrary third-party apps.

## Release artifact boundary

The Research Preview release contains source archives, a Unity Package Manager
tarball, Kotlin protocol JAR, Android motion-source AAR, ILXR/1.0 specification,
license/notices, and `SHA256SUMS`. It intentionally excludes the unsigned
release APK, debug-signed APK, keystore/signing key, pairing key, packet capture,
real-trip recording, and human-subject data.

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
