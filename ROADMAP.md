# Roadmap

This roadmap describes engineering gates, not release dates. Items move only
after their safety, security, and maintenance costs are understood.

## v0.1 — Research Preview

- [x] Specify authenticated ILXR/1.0 IMU and clock-sync datagrams.
- [x] Provide dependency-free conformance and synthetic-signal tools.
- [ ] Validate Android-to-Unity interoperability on at least two Android sensor
  implementations and one OpenXR headset.
- [ ] Confirm stale, replayed, malformed, and out-of-range data cannot drive
  content or refresh the safety timer.
- [ ] Publish reproducible end-to-end latency and packet-loss methodology.
- [ ] Complete stationary, synthetic-signal, and tracking-loss checks.

## v0.2 — Integrator preview

- [ ] Stabilize the Unity sample API and content-root guard.
- [ ] Add mount profiles with explicit coordinate-transform tests.
- [ ] Add deterministic recording/replay in Android and Unity without location
  or personal metadata.
- [ ] Publish cross-device interoperability fixtures.
- [ ] Document accessibility and comfort controls such as per-axis gain,
  dead-zone, cue selection, and immediate disable.

## Later research

- Evaluate external-phone and headset-only vehicle-reference approaches.
- Compare latency, jitter, sign, gain, and dropout behavior using preregistered
  protocols and appropriate ethics review.
- Explore transports behind the same sample API without weakening local-first
  behavior or authenticated-by-default transport.
- Consider a stable 1.0 protocol only after independent implementations exist.

## Explicitly out of scope

- Claims that this software prevents, treats, or guarantees reduction of
  motion sickness.
- Driver, pedestrian, cyclist, or machinery-operator use.
- Hidden/system-wide overlays over unrelated Quest or OpenXR applications.
- Automatic camera or XR Origin motion.
- Location tracking, advertising, telemetry, user accounts, or cloud storage.
- Full inertial navigation or reconstruction of a vehicle's route.
