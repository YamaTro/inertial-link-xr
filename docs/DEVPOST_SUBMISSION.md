# Devpost submission copy

This file is the reviewed English copy for the OpenAI Build Week submission.
It records only results supported by the repository or the local validation
record. The non-public `/feedback` session ID belongs in the Devpost form, not
in this public repository.

## Project name

InertialLink XR

## Elevator pitch

Turn a vehicle-mounted Android phone into a secure external motion reference
for Unity/OpenXR, without replacing head tracking or collecting location data.

## Category

Developer Tools

## Project story

### Inspiration

Passengers using VR in a moving bus or car face a reference-frame problem: the
vestibular system feels the vehicle turn, accelerate, and brake while a virtual
environment may show none of it. A headset IMU also mixes vehicle motion with
intentional head movement.

Peer-reviewed on-road and on-vehicle studies report lower motion sickness when
vehicle-synchronized peripheral or background visual motion is added to VR.
That research motivates InertialLink XR. This project does not claim that its
implementation has independently demonstrated sickness reduction.

### What it does

InertialLink XR is an open protocol, Android sender, and Unity package that
keeps vehicle motion separate from normal OpenXR head tracking.

The Android side reads acceleration, gyroscope, gravity, linear acceleration,
and rotation-vector sensors. It sends bounded samples over a local UDP link
using the versioned ILXR/1.0 protocol. Packets use an ephemeral 128-bit pairing
key, truncated HMAC-SHA-256 authentication, replay rejection, clock
synchronization, strict numeric and age limits, and stale-data rejection.

The Unity package validates that stream and exposes vehicle motion without
moving the Camera or XR Origin. Its optional drivers accept only an explicitly
assigned safe content root and fade to neutral when valid input stops.

The main demo fixes a 9:16 video in front of a curved, perspective-converging
star grid. The grid responds to authenticated vehicle acceleration and yaw,
while the video and camera remain unchanged. A second component compares
physical acceleration with an application's virtual acceleration and reports a
bounded correction suggestion; it never controls a camera, actuator, or
vehicle.

### Real phone-to-Unity proof

On 2026-07-22, a Xiaomi XIG04 running Android 15/API 35 sent authenticated
motion packets over a private local network to Unity 6000.1.14f1. Unity accepted
216 packets, rejected 0, dropped 0 frames, synchronized clocks with a 5.448 ms
best round trip, played the local portrait video through frame 112, rendered
2,019 directional cues, and left the Camera pose unchanged. The evidence file
contains no pairing key or real-trip recording.

This proves the stationary phone-to-PC transport, authentication, Unity runtime
path, video playback, cue generation, and camera-safety behavior. It does not
prove headset compatibility, in-vehicle comfort, or human efficacy.

### How we built it with Codex and GPT-5.6

I set the product and safety constraints: use a vehicle-fixed phone as an
external reference, preserve head tracking, keep data local, require opt-in
content, fail closed, and distinguish prior research from our own evidence.

Codex with GPT-5.6 helped decompose the idea into the ILXR protocol, Kotlin
Android modules, a C# Unity package, Node tooling, negative security tests,
release automation, and a reproducible presentation path. It accelerated
cross-language test-vector alignment, Android and Unity integration debugging,
failure-path coverage, the directional star-grid sample, and the real Xiaomi
validation while keeping credentials and raw sensor data out of the repository.

### Challenges

The first challenge was ownership of motion. Applying phone motion to the XR
camera would fight head tracking, so the package exposes data or moves only a
deliberately assigned safe visual root.

The second was presenting motion without covering the task. The final sample
keeps a portrait video fixed and places the motion signal in a curved background
that never overlays the content.

The third was accepting low-latency UDP without trusting it. Authentication,
sequence checks, freshness bounds, numeric validation, rate limits, and neutral
fade-out form a fail-closed boundary.

The fourth was honest evidence. Synthetic tests, a normal Unity Play Mode media
run, and a real phone-to-Unity run are documented separately. No OpenXR headset,
vehicle ride, or human-subject result is claimed.

### What we learned

Vehicle motion and headset-relative head motion should remain separate
reference frames. Useful cues do not require moving the whole world. A narrow,
auditable integration surface makes both research prototyping and existing-app
adoption easier. Evidence is strongest when every statement identifies whether
it comes from prior literature, automated tests, a local runtime demonstration,
or a still-unperformed human trial.

### Why it matters beyond motion-sickness research

The durable contribution is not a single comfort effect; it is a working,
authenticated external-motion reference that an XR application can consume
without surrendering head tracking. The same separation can support
physical-versus-virtual acceleration diagnostics, digital-twin alignment,
motion-platform and simulator calibration, moving-cabin training systems, and
repeatable QA for content that must react to a real platform. InertialLink XR
already exposes the bounded mismatch signal needed for those workflows while
leaving every correction reviewable and application-controlled.

That makes the project useful even if a future passenger study finds that one
cue design helps only some users: the protocol, security boundary, calibration
path, reference-frame separation, and diagnostic layer remain reusable OSS
infrastructure.

### What's next

Next are OpenXR headset validation, user-adjustable cue strength and polarity,
longer stationary reliability tests, digital-twin and simulator adapters, and
controlled vehicle trials. Only after those steps should carefully governed
passenger research begin, with informed consent, stop criteria, individual
tuning, and appropriate review.

## Built with

Android, Kotlin, Unity, OpenXR, C#, Node.js, UDP, HMAC-SHA-256, Codex, GPT-5.6

## Public links

- Repository: https://github.com/YamaTro/inertial-link-xr
- Latest release: https://github.com/YamaTro/inertial-link-xr/releases/tag/v0.2.0

## Judge testing instructions

1. Clone the repository and run `npm run check` with Node.js 24 or newer. This
   dependency-free path exercises protocol, mutation, recording, Markdown-link,
   and pinned-relay checks without hardware.
2. Open `unity/ValidationProject` in Unity 2022.3 LTS or newer and run the
   package Edit Mode tests.
3. In Package Manager, import **Vertical Video + Directional Star Grid**.
4. Add `VerticalVideoCueDemoBootstrap` to an empty GameObject and optionally
   assign a local portrait `VideoClip`.
5. Enter Play Mode. The fixed video and Camera should remain unchanged while
   the generated directional background responds to the synthetic source.

For an authenticated device test, follow `docs/android-setup.md`,
`docs/integration.md`, and `docs/calibration.md`. Start stationary on a private
network. Do not begin with a public road or passenger trial.

## Demonstration video

The final public video is 2 minutes 44 seconds, uses English synthetic speech,
contains no music, and distinguishes prior research from project evidence. It
shows the real Xiaomi-to-Unity screenshot and the verified metrics above. The
local MP4 and narrated deck remain ignored build outputs; only the reviewed
source, screenshot, and validation claims belong in the repository.

## Research references

- Noh, Park, and Kim, *IEEE Access* 12 (2024),
  [doi:10.1109/ACCESS.2024.3408834](https://doi.org/10.1109/ACCESS.2024.3408834)
- Kim et al., *Proceedings of the Human Factors and Ergonomics Society Annual
  Meeting* 66(1) (2022),
  [doi:10.1177/1071181322661086](https://doi.org/10.1177/1071181322661086)
- McGill, Ng, and Brewster, CHI 2017,
  [doi:10.1145/3025453.3026046](https://doi.org/10.1145/3025453.3026046)
- Qiu et al., *Applied Ergonomics* 135 (2026),
  [doi:10.1016/j.apergo.2026.104778](https://doi.org/10.1016/j.apergo.2026.104778)
