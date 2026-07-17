# Security policy

InertialLink XR moves virtual content in response to network-delivered sensor
data. Authentication, input validation, stale-data handling, and safe fallback
are security boundaries—not optional polish.

## Supported versions

This repository is currently a **Research Preview**. Until the first stable
release, only the latest commit on `main` and the newest tagged preview receive
security fixes. No release is suitable for safety-critical use.

## Report a vulnerability privately

Please use the repository's **Security → Report a vulnerability** form to open
a private GitHub Security Advisory. Do not file a public issue, attach packet
captures, publish a proof of concept, or include a real pairing key in a report.

Include, when possible:

- affected commit or release;
- affected Android, Unity, and headset versions;
- a minimal reproduction using synthetic data;
- expected and observed safe-fallback behavior;
- impact and whether exploitation requires access to the local network; and
- any suggested remediation.

Maintainers will acknowledge a complete report as capacity permits, coordinate
a fix and advisory, and credit reporters who want attribution. There is no
bug-bounty program and no guaranteed response deadline.

## Security guarantees and non-guarantees

The reference sender requires a fresh 16-byte pairing key for every sender
session and rotates it after a failed start or return from background. Packets
are authenticated with the first 16 bytes of HMAC-SHA-256,
checked before decoding motion, and screened for replays, invalid values,
future timestamps, and staleness. Invalid packets never extend the output's
safety timer.

HMAC provides **integrity and peer authentication, not confidentiality**.
Anyone able to observe the local link can read motion values and traffic
timing. Use a private USB/Wi-Fi link or a trusted encrypted tunnel where that
metadata is sensitive. UDP also cannot guarantee delivery or prevent a local
attacker from causing denial of service.

The project deliberately has no discovery broadcast, cloud service, analytics,
camera, GPS, microphone, or account requirement. Android builds should request
only the minimum network and foreground-service permissions documented in
[the threat model](docs/threat-model.md).

## Integrator responsibilities

- Keep authentication required everywhere. Loopback tests may use the documented
  public test key, but they do not disable packet authentication.
- Generate pairing keys with a cryptographically secure random generator;
  never commit, log, reuse, or include them in recordings.
- Do not put private keys in command-line arguments. The Node tools accept
  `--key` only as a loopback convenience for public vectors; non-loopback use
  requires `ILXR_PAIRING_KEY`, which is removed from the tool environment after
  parsing. Environment variables still do not protect against a compromised
  same-user process, so use a dedicated test account/link where appropriate.
- Bind to the narrowest interface practical and use a trusted local link.
- Preserve packet length, HMAC, replay, age, and numeric-range checks.
- Drive only an isolated content root. Never bypass the Camera/XR Origin guard.
- Preserve the 250 ms stale threshold and 250 ms fade unless an evidence-backed
  safety review supports a different value.
- Treat recordings, issue content, logs, packet data, and calibration profiles
  as untrusted input.

See [Threat model](docs/threat-model.md), [Protocol](protocol/SPEC.md), and
[Safety](docs/safety.md) for the full boundary.
