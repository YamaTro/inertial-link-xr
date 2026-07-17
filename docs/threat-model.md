# Threat model

Status: Research Preview. Review this model whenever the protocol, permissions,
data retention, receiver target selection, or dependency graph changes.

## Assets and safety properties

| Asset / property | Required protection |
| --- | --- |
| Scene motion integrity | Only fresh samples from the paired sender may drive output |
| Safe fallback | Missing or invalid input returns output to neutral within 500 ms |
| Head tracking | The package never modifies Camera or XR Origin ancestry |
| Pairing key | Never transmitted in-protocol, logged, committed, or persisted by default |
| Passenger privacy | No GPS, camera, microphone, account, telemetry, or real-trip fixture |
| Application availability | Malformed traffic cannot cause unbounded allocation or main-thread blocking |

## Trust boundaries

1. **Physical sensor boundary.** Android sensors and mounts are noisy, can be
   misoriented, and may be malicious on a compromised phone.
2. **Sender process boundary.** Other apps or local debuggers may inspect memory
   on a rooted/compromised device. That is outside the pairing-key guarantee.
3. **Local network boundary.** Wi-Fi/USB networks can drop, reorder, delay,
   observe, replay, or inject UDP datagrams.
4. **Parser boundary.** Lengths, floats, flags, timestamps, and status bits are
   attacker-controlled until authenticated and validated.
5. **Unity scene boundary.** A valid sample can still be unsafe when an
   integrator applies the wrong sign, gain, target, or cue.
6. **Recording/contribution boundary.** Files, issues, logs, and pull requests
   can contain secrets, personal data, or instructions intended to trigger
   unsafe automation.

## Adversaries and failures

| Scenario | Mitigation | Residual risk |
| --- | --- | --- |
| Unpaired LAN host injects motion | Fresh 128-bit key; HMAC checked before decode; endpoint/session pinning | Key shown to or stolen by attacker defeats peer authentication |
| Attacker replays a valid turn packet | Random session ID, per-source-endpoint non-wrapping u32 sequence, bounded numeric replay window, event-age check | A compromised sender can emit new authenticated bad data |
| Delayed/reordered/lost UDP | Authenticated clock sync, age/future limits, replay window, warm-up and fade | UDP has no availability guarantee; attacker can jam/drop traffic |
| Oversized/truncated/malformed packet | 512-byte receive ceiling, exact lengths, checked reads, finite/range/status validation | Implementation bugs remain possible; fuzzing is required |
| Timing/key side channel | Constant-time HMAC compare; generic/rate-limited errors; no packet echo | Host/runtime side channels are not comprehensively addressed |
| Passive network observation | None at ILXR layer; use private link or trusted encrypted tunnel | HMAC does not conceal motion or timing |
| Discovery leaks presence | No broadcast, multicast, service registry, or cloud rendezvous | IP/MAC traffic is still visible on the local link |
| Stale last sample keeps content moving | Invalid input cannot refresh timer; fade begins at 250 ms, neutral at 500 ms | An authenticated compromised sender can keep sending plausible values |
| Wrong mount/sign/gain worsens discomfort | Explicit transform, calibration generation, synthetic axis checks, bounded driver, immediate disable | Human response varies; no calibration proves efficacy |
| Driver moves headset/camera | Driver refuses Camera/XR Origin hierarchy and acts only on explicit root | Custom integrator code can ignore the guard |
| Real trip/person data committed | Synthetic-only schema, strict validator, contribution rules, ignore local recordings | Reviewers must still inspect diffs and repository history |
| Dependency/supply-chain compromise | Standard libraries where practical, pinned CI actions, least permissions, Dependabot review | Android/Unity toolchains and platforms remain trusted dependencies |

## Protocol assumptions

- Pairing transfers the key through a trusted human-visible path.
- Both endpoints have secure random generation and uncompromised HMAC-SHA-256.
- Monotonic clocks progress normally during a session.
- The content application preserves the receiver and target-selection guards.
- The passenger can immediately remove/disable the headset and is not operating
  a vehicle or machinery.

An IP address, Wi-Fi SSID, device name, calibration ID, or session ID is not a
secret and is not proof of identity.

## Deliberate non-goals

- Confidentiality inside ILXR itself
- Protection after sender or receiver compromise
- Network availability or anti-jamming
- Geographic position, route, or absolute yaw truth
- Safety certification, medical efficacy, or autonomous control
- System-wide overlays or control of unrelated applications

## Security test obligations

Every parser implementation should cover short and oversized datagrams, length
mismatch, unknown versions/types/flags, incorrect and truncated HMAC, wrong key,
sequence replay and reordering, session changes, clock overflow/skew, stale and
future times, every NaN/infinity representation, numeric limits, invalid
quaternions, reserved status bits, and dropout during motion. Fuzzers must run
offline or on loopback and must not replay public issue attachments directly.

See [SECURITY.md](../SECURITY.md) for private reporting.
