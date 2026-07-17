# InertialLink XR

**Research Preview — not a medical device and not proven to prevent motion sickness.**

InertialLink XR is an open protocol, Android motion sender, and Unity/OpenXR
package for driving an explicitly selected virtual environment root from a
vehicle-mounted phone. It keeps the headset's normal head tracking separate
from vehicle motion and gives researchers and XR developers a small,
auditable integration surface.

[日本語](README.ja.md) · [Protocol](protocol/SPEC.md) ·
[Integration guide](docs/integration.md) · [Safety](docs/safety.md) ·
[Security](SECURITY.md)

> **Passenger use only.** Never use or configure a headset while driving,
> cycling, walking, or controlling machinery. Stop immediately if you feel
> discomfort. The project makes no claim that its cues prevent, treat, or
> reduce sickness for any particular person.

## What this project provides

- An Android sender using acceleration, gyroscope, gravity, linear
  acceleration, and rotation-vector sensors—without camera, GPS, microphone,
  account, cloud, advertising, or telemetry permissions.
- A versioned, big-endian UDP protocol with an ephemeral 128-bit pairing key,
  truncated HMAC-SHA-256 authentication, replay rejection, clock sync, strict
  bounds, and stale-data rejection.
- A Unity 2022.3 LTS+ package that exposes validated motion samples and can
  drive only a deliberately assigned content root. It refuses to drive a
  Camera or XR Origin hierarchy.
- Dependency-free Node.js tools for deterministic synthetic signals, packet
  inspection, protocol tests, and recording validation.
- Synthetic recordings and documentation for repeatable development without
  collecting real passenger or location data.

It does **not** provide a Quest-wide/system overlay, replace OpenXR head
tracking, infer location, reconstruct a vehicle trajectory, or make arbitrary
third-party applications move. Applications opt in and choose exactly which
content consumes motion data.

## Architecture

```text
vehicle-fixed Android phone                 XR application
┌──────────────────────────┐    local UDP   ┌────────────────────────────┐
│ sensors → mount transform├───────────────►│ authenticated receiver     │
│ ephemeral pairing key    │   ILXR v1.0    │ replay/age/range checks    │
└──────────────────────────┘◄───────────────┤ clock synchronisation      │
                                            │ VehicleMotionHub           │
headset tracking ──────────────────────────►│ OpenXR Camera/XR Origin    │
                                            │ selected contentRoot only  │
                                            └────────────────────────────┘
```

The wire coordinate system is OpenXR-style right-handed: +X right, +Y up,
and -Z forward. All transport values use SI units. See the normative
[protocol specification](protocol/SPEC.md).

## Start safely with synthetic data

Requirements: Node.js 24+ and Unity 2022.3 LTS or newer. Android development
additionally requires JDK 17 and the Android SDK.

1. Generate a temporary 16-byte key. Do not reuse this documentation value
   outside loopback testing:

   ```text
   00112233445566778899AABBCCDDEEFF
   ```

2. In one terminal, run the loopback-only inspector:

   ```sh
   node tools/packet-inspector.mjs --key 00112233445566778899AABBCCDDEEFF
   ```

3. In another terminal, send a bounded synthetic turn:

   ```sh
   node tools/synthetic-sender.mjs --key 00112233445566778899AABBCCDDEEFF --scenario gentle-turn --seconds 10
   ```

4. Run the repository checks:

   ```sh
   npm run check
   ```

Both tools bind to loopback by default and refuse non-loopback networking
unless `--allow-network` is explicitly supplied. Non-loopback Node use also
requires the one-session key in `ILXR_PAIRING_KEY`; `--key` is reserved for
public-vector loopback tests. They never read device sensors.

Next, follow [Unity integration](docs/integration.md), then
[Android setup](docs/android-setup.md), and complete the
[calibration checklist](docs/calibration.md) before any stationary-vehicle
test. Do not begin with a public-road or public-transport trial.

## Repository layout

| Path | Purpose |
| --- | --- |
| `android/` | Kotlin protocol library, Android motion source, and sender app |
| `unity/` | Unity Package Manager package and tests |
| `protocol/` | Normative wire-format specification |
| `tools/` | Dependency-free synthetic sender, inspector, and validators |
| `recordings/` | Synthetic-only example recordings |
| `docs/` | Architecture, integration, calibration, safety, and threat model |

The exact automated and manual verification coverage—and the gaps that remain—is
recorded in [Validation](docs/VALIDATION.md).

## Design boundaries

**Safety before motion fidelity.** Invalid, unauthenticated, replayed, future,
or stale packets cannot refresh the receiver's safety timer. When valid input
stops, output fades to neutral; it never holds the last motion indefinitely.

**Private by default.** Motion stays on the local link. There is no discovery
broadcast, remote service, analytics, GPS, or user account. HMAC authenticates
packets but does not encrypt them; use a private link or a trusted tunnel if
motion-data confidentiality matters.

**Integration, not global control.** The Unity driver modifies only the
assigned content transform. It must not be placed above a Camera, XR Origin,
or unrelated application UI. See [limitations](docs/limitations.md).

## Project status

The v0.1 goal is interoperability and safe failure behavior, not an efficacy
claim. APIs and wire-protocol minor versions may evolve. Compatibility rules
are documented in the [specification](protocol/SPEC.md), and planned work is
tracked in the [roadmap](ROADMAP.md).

Security reports should follow [SECURITY.md](SECURITY.md). General changes are
welcome under [CONTRIBUTING.md](CONTRIBUTING.md) and the
[Code of Conduct](CODE_OF_CONDUCT.md).

## License

Apache License 2.0. See [LICENSE](LICENSE), [NOTICE](NOTICE), and
[third-party notices](THIRD_PARTY_NOTICES.md).
