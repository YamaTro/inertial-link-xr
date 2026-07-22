# InertialLink XR: Build Week project showcase

[日本語](showcase.ja.md) · [Main README](../README.md) ·
[Devpost](https://devpost.com/software/inertial-link-xr) ·
[Demo video](https://www.youtube.com/watch?v=cqnwPqBBy8E) ·
[v0.2.0 Research Preview](https://github.com/YamaTro/inertial-link-xr/releases/tag/v0.2.0)

![InertialLink XR connects a mounted Android phone to an opt-in XR content layer](assets/devpost-thumbnail.png)

## The short version

InertialLink XR turns a vehicle-mounted Android phone into an authenticated,
external motion reference for Unity/OpenXR. An application can use that
reference to move a deliberately selected background or peripheral cue while
normal headset tracking, the camera, and primary content remain untouched.

The Build Week result is a working phone-to-Unity integration, not a concept
render. Its motion-sickness efficacy has **not** been tested on people, and it
is not a medical device.

## The problem and the hypothesis

A passenger's vestibular system can feel a vehicle turn or accelerate while
the visual content inside a headset appears stationary. Prior vehicle studies
have reported lower sickness when peripheral or background motion is
synchronized with vehicle motion. Other work shows why the implementation
needs personal tuning: no single visual treatment best balances sickness,
distraction, and immersion for everyone.

InertialLink XR does not attempt to prove that research again. It provides the
missing reusable engineering layer:

1. mount a phone to the vehicle so it measures the vehicle rather than the
   passenger's head;
2. authenticate and validate that motion over a local link;
3. preserve OpenXR head tracking; and
4. expose bounded data to an opt-in content root or diagnostic component.

See [Research basis and claim boundary](research-basis.md) for the 2022 HFES,
2024 IEEE Access, 2017 CHI, and 2026 Applied Ergonomics studies.

## What was built

| Layer | Build Week result |
| --- | --- |
| Android | A sensor source and sender for acceleration, gyroscope, gravity, linear acceleration, and rotation vector, with a mount transform and no GPS, camera, microphone, account, cloud, ads, or telemetry. |
| Local protocol | `ILXR/1.0`, a versioned UDP format with an ephemeral 128-bit pairing key, truncated HMAC-SHA-256, clock synchronization, replay/age/range checks, and shared Kotlin/C#/Node test vectors. |
| Unity/OpenXR | A package that publishes validated samples and can drive only an explicitly assigned content root. It refuses Camera and XR Origin hierarchies. |
| Visual demo | A fixed 9:16 video surrounded by a curved, perspective-converging field of directional cues driven by authenticated phone motion. |
| Diagnostics | A report-only monitor that compares measured physical acceleration with virtual acceleration and suggests a bounded correction. |
| Reproducibility | Dependency-free Node tools, synthetic recordings, failure-path tests, integration guides, and release automation. |

## The vertical-video demonstration

![Unity receiving authenticated Xiaomi 13T motion while the vertical video and Camera remain fixed](assets/xiaomi13t-unity-demo.png)

The demonstration models a passenger watching short-form vertical video. The
central 9:16 panel stays fixed and readable. Motion is expressed only in the
surrounding field, where curved perspective lines and star-like cues make the
direction of travel visually legible without moving the primary video.

In the stationary Xiaomi 13T-to-Unity run:

- 216 authenticated packets were accepted;
- 0 packets were rejected or dropped;
- the best synchronized round trip was 5.448 ms;
- the local video reached frame 112;
- 2,019 directional cues were active; and
- the Unity Camera pose stayed unchanged.

This proves the implemented data path and rendering behavior under that test
setup. It does not prove comfort, sickness reduction, headset compatibility,
or behavior in a moving vehicle.

## Two reference frames, kept separate

```text
mounted Android phone                     opt-in Unity application
┌──────────────────────────┐   local UDP   ┌────────────────────────────┐
│ vehicle acceleration     ├──────────────►│ authenticated receiver     │
│ angular velocity         │   ILXR/1.0    │ age/replay/range checks    │
│ gravity + orientation    │               │ selected contentRoot only │
└──────────────────────────┘               └────────────────────────────┘

OpenXR headset tracking ─────────────────► Camera / XR Origin (unchanged)
```

This separation is the central design choice. A headset sensor observes both
vehicle motion and intentional head motion. The mounted phone supplies a
vehicle-fixed reference, while OpenXR remains responsible for the passenger's
head-relative view.

## Measuring physical–virtual mismatch

The `MotionAlignmentMonitor` lets simulator and digital-twin developers compare
the acceleration measured by the phone with the acceleration represented by an
application. In the recorded synthetic Unity case:

| Value | X-axis result |
| --- | ---: |
| Measured physical acceleration | +1.134 m/s² |
| Deliberately under-responsive virtual acceleration | +0.816 m/s² |
| Reported mismatch | 0.318 m/s² |
| Bounded correction suggestion | +0.159 m/s² |

The sample rule was
`clamp((measured - virtual) × 0.5, -2.0, +2.0)`. The result is diagnostic only:
it never controls a vehicle, motion platform, Camera, XR Origin, or application
transform automatically.

## Security and privacy boundaries

Motion packets are untrusted input. The receiver fails closed:

- a wrong key or tampered HMAC is rejected;
- replayed, old, or implausibly future-dated packets are rejected;
- NaN, infinity, and out-of-range values are rejected;
- invalid input cannot refresh the safety timer; and
- when valid input stops, the output smoothly fades to neutral instead of
  holding the last motion forever.

The normal path is local and minimal: no discovery broadcast, GPS, account,
cloud service, telemetry, advertising, camera, or microphone. HMAC authenticates
the packets but does not encrypt them, so a private link or trusted tunnel is
needed when motion-data confidentiality matters. See the [threat model](threat-model.md)
and [security policy](../SECURITY.md).

## Why the same primitive matters beyond passenger comfort

The reusable product is an authenticated physical-motion reference, not one
particular visual effect. Potential extensions include:

- **digital-twin alignment:** compare real equipment motion with its virtual
  counterpart and identify drift;
- **simulator calibration:** quantify how closely a training scene represents
  a measured physical maneuver;
- **motion-platform QA:** compare commanded or rendered acceleration with an
  independently measured reference;
- **location-based XR:** keep environmental effects synchronized to a moving
  cabin without replacing headset tracking; and
- **research instrumentation:** prototype and compare peripheral cue geometry,
  gain, polarity, thresholds, and latency compensation.

These are technically supported directions, not claims that every application
has already been built or validated.

## Evidence boundary

| Verified in v0.2.0 | Not yet verified |
| --- | --- |
| 21/21 Node protocol and security tests | Any reduction in motion sickness |
| 24/24 dependency-free C# core checks | Human comfort, accessibility, or adverse effects |
| Android debug and release builds | A bus/car passenger trial |
| 19/19 Unity Edit Mode tests | Meta Quest or another OpenXR headset run |
| Normal Unity Play Mode vertical-video demo | Long-duration thermal/background operation |
| Stationary Xiaomi 13T-to-Unity authenticated link | Independent security audit or exhaustive fuzzing |

The detailed commands, environments, results, and limitations are recorded in
[Validation](VALIDATION.md).

## Built with Codex and GPT-5.6

The builder defined the core hypothesis, safety boundaries, and product
decisions. During OpenAI Build Week, Codex with GPT-5.6 helped turn those choices
into a cross-language protocol, Android sender, Unity package, tests, visual
demo, documentation, and release automation. It was particularly useful for
keeping Kotlin, C#, and Node implementations aligned and converting security
requirements into executable negative tests.

The complete division of work and dated repository evidence is in the
[Build Week collaboration record](OPENAI_BUILD_WEEK.md).

## Try it safely

Start with the loopback-only synthetic path in the [main README](../README.md),
then follow [Unity integration](integration.md), [Android setup](android-setup.md),
and the [calibration checklist](calibration.md). Begin with synthetic input and
a stationary setup—never with a public-road or public-transport trial.

Passenger use only. Stop immediately if discomfort occurs. Do not use or
configure a headset while driving, cycling, walking, or controlling machinery.
