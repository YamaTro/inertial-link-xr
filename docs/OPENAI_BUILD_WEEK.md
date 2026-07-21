# OpenAI Build Week collaboration record

InertialLink XR was created during the OpenAI Build Week submission period. The
initial research-preview implementation was committed on 2026-07-17, with
release-verification follow-ups on 2026-07-19.

This record describes how the project was built with Codex and GPT-5.6. It is
not evidence of medical efficacy, physical-device compatibility, or a completed
vehicle trial.

## Human direction

The builder supplied the original product goal and made the consequential
product decisions:

- use a phone fixed to the vehicle as an external IMU reference;
- keep vehicle motion separate from headset-relative head tracking;
- design an OSS integration surface that existing Unity/OpenXR applications can
  adopt instead of making a closed, single-purpose experience;
- keep all motion data local and avoid GPS, accounts, telemetry, advertising,
  camera, and microphone access;
- modify only a deliberately assigned content transform rather than the camera,
  XR Origin, or unrelated UI; and
- publish as a Research Preview with no claim that it prevents, treats, or
  reduces motion sickness.

## How Codex and GPT-5.6 were used

Codex with GPT-5.6 served as the implementation and review partner in the main
project thread. It helped:

1. translate the product constraints into a repository plan and threat model;
2. specify a compact, versioned, big-endian UDP protocol;
3. implement compatible encoders, decoders, and test vectors in Kotlin, C#, and
   Node.js;
4. build an Android sensor/sender boundary and a Unity package with explicit
   content-root safety checks;
5. add authentication, replay rejection, clock synchronization, age and numeric
   bounds, rate limiting, and stale-data fade-out;
6. create deterministic synthetic recordings, a loopback sender, a packet
   inspector, and cross-language failure-path tests;
7. review Android permissions, release contents, dependency verification, and
   GitHub Actions supply-chain pins; and
8. implement protected-margin visual cues and a report-only physical/virtual
   acceleration alignment monitor;
9. build and validate a real Unity Play Mode sample with a fixed 9:16 video,
   synthetic vehicle motion, and explicit camera-pose evidence;
10. turn the margin prototype into a curved, perspective directional motion
    dome based on the builder's visual reference; and
11. diagnose the Android UI-thread socket failure, add a bounded debug-only
    validation path, and complete a real Xiaomi-to-Unity authenticated run.

Codex was especially useful for keeping the protocol implementations aligned
while iterating across three languages, and for turning security and safety
requirements into executable negative tests rather than prose alone.

## Challenges and decisions

### Vehicle motion versus head motion

The headset sensor alone cannot cleanly distinguish vehicle motion from the
passenger's intentional head movement. The architecture therefore treats the
mounted phone as the vehicle reference while leaving OpenXR head tracking
untouched.

### Motion data is untrusted input

UDP is useful for a low-latency local link, but raw packets must not drive a
scene graph. The receiver authenticates packets with an ephemeral pairing key,
rejects malformed, replayed, stale, future, or out-of-range samples, and fades
to neutral when valid input stops. HMAC provides authenticity, not
confidentiality; a private network or trusted tunnel is still required when
motion-data confidentiality matters.

### Honest validation across distinct evidence levels

The project first ran 19 Edit Mode tests and a normal Editor Play Mode media
check with synthetic input. It then completed a stationary Xiaomi XIG04 to
Unity 6000.1.14f1 run: 216 authenticated packets were accepted, none were
rejected or dropped, the best synchronized round trip was 5.448 ms, the local
video reached frame 112, 2,019 directional cues were active, and the Camera pose
stayed unchanged. No pairing key or raw trip data was saved.

The build environment still did not include an OpenXR headset, vehicle cabin,
or human-subject study. The verified results establish implementation mechanics
and the phone-to-PC Unity path, not comfort or medical efficacy. The exact
verified and unverified boundaries are documented in
[`VALIDATION.md`](VALIDATION.md).

## Reproducible judge path

The smallest no-hardware test uses only Node.js 24+ and the public loopback test
key. It does not access real sensors or a network interface outside the local
machine.

Terminal 1:

```sh
node tools/packet-inspector.mjs --key 00112233445566778899AABBCCDDEEFF
```

Terminal 2:

```sh
node tools/synthetic-sender.mjs --key 00112233445566778899AABBCCDDEEFF --scenario gentle-turn --seconds 10
```

Full dependency-free checks:

```sh
npm run check
```

For Unity and Android integration, follow the main README and the platform
guides. Start with synthetic input and a stationary setup; do not begin with a
public-road or passenger trial.

## Dated repository evidence

The public Git history and tagged Research Preview show the work performed
during the submission period:

- `ebffdae` — initial Research Preview, 2026-07-17;
- `9e2a1f3` — Gradle verification metadata, 2026-07-19;
- `3db3e2d` — Linux `aapt2` verification, 2026-07-19; and
- `8e0b408` — Unity changelog release alignment, 2026-07-19.

The Devpost form contains the `/feedback` session ID for the main Codex build
thread. The public repository intentionally contains no private conversation
content, credentials, pairing secrets, real-trip recordings, or personal data.
