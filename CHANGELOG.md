# Changelog

All notable project changes will be documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html) for releases.

## [Unreleased]

## [0.2.0] - 2026-07-22

### Added

- Curved `DirectionalMotionDome` sample that keeps a 9:16 video and XR camera
  fixed while authenticated vehicle motion drives a perspective background.
- Report-only physical-versus-virtual acceleration alignment monitor.
- Real Xiaomi XIG04-to-Unity validation harness and documented evidence
  boundary.
- Pinned-endpoint, bounded local UDP relay for Windows firewall-constrained
  development without changing firewall configuration.

### Fixed

- Move Android UDP socket setup off the UI thread and bound setup time so the
  real sender does not fail with `NetworkOnMainThreadException`.

### Security

- Keep automated device controls debug-only, limit the validation wake lock to
  five minutes, and exclude `WAKE_LOCK` from the release APK.

## [0.1.0] - 2026-07-17

### Added

- Initial Research Preview Android sender, Unity/OpenXR integration package,
  authenticated ILXR/1.0 protocol, synthetic tools, tests, and safety/security
  documentation.

### Security

- Ephemeral 128-bit pairing keys, truncated HMAC-SHA-256 packet
  authentication, replay protection, clock synchronisation, numeric bounds,
  and stale-input fade-out.

[Unreleased]: https://github.com/YamaTro/inertial-link-xr/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/YamaTro/inertial-link-xr/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/YamaTro/inertial-link-xr/releases/tag/v0.1.0
