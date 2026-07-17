# Calibration and axis verification

Calibration establishes a neutral sensor bias and verifies one explicit phone
mount. It does not determine a comfortable cue, prove efficacy, or make IMU
position integration reliable.

## Reference frame

ILXR uses a right-handed OpenXR-style vehicle frame:

```text
          +Y up
            │
            │
forward -Z ─┼── +X right
```

The built-in `SCREEN_UP_TOP_FORWARD` profile assumes the phone screen faces up
and its top edge points toward the front of the vehicle. Android device vectors
map as `(x, y, z) → (x, z, -y)`. Any other mount requires an explicit proper
rotation and tests; do not “fix” axes independently in Unity.

## Before calibration

- Perform the first checks on a desk with no one wearing the headset.
- Confirm the phone is not charging, vibrating, or resting on a flexible mount.
- Confirm the application has no Camera/XR Origin under or above `contentRoot`.
- Start with driver gains at zero. Keep an immediate disable action available.
- Use a new pairing key and verify Unity reports the intended sender and session.

## Stationary bias calibration

1. Place the phone in its final rigid orientation on a stationary surface or
   stationary vehicle. Do not hold it.
2. Keep the phone and mount completely still.
3. Start the sender and request **Stationary calibration**.
4. Do not touch the phone until the sender reports completion. Movement causes
   the attempt to fail rather than accepting a moving bias.
5. Confirm `CALIBRATED` is set, `CALIBRATING` is clear, and `calibrationId`
   changed. Unity should return to warm-up on that change.
6. At rest, verify corrected angular velocity and linear acceleration remain
   near zero without a steady drift. Gravity magnitude should be near local `g`,
   but it is an estimate and need not equal exactly 9.80665 m/s².

Recalibrate after moving the mount, changing phones, restarting if the app uses
an in-memory calibration, or observing a persistent bias. Do not recalibrate
while a vehicle is moving.

## Sign and axis test

Run this without wearing the headset and watch numeric diagnostics or an obvious
test object—not a subtle comfort cue.

1. Return the system to neutral between each motion.
2. Move/tilt the unmounted test phone briefly toward its marked vehicle-right
   direction. Only the expected +X channel should dominate.
3. Repeat for marked up (+Y) and forward (-Z).
4. Rotate slowly about each marked positive axis and verify gyroscope sign.
5. Run the deterministic `gentle-turn` synthetic source and confirm Android,
   Node inspector, Unity diagnostics, and scene behavior agree.
6. Verify the same test with an intentionally wrong key, duplicate packet,
   delayed packet, and sender stop. None may sustain output.

Accelerometer readings during a hand movement include gravity and are not a
precise force reference. The purpose is detecting swaps and sign inversions.

## Content policy calibration

Transport calibration and visual-response tuning are separate. After transport
passes, an integrator may tune an isolated cue under a reviewed test plan:

- begin at gain zero;
- change one axis/parameter at a time;
- impose translation and rotation clamps;
- record software version, mount profile, filter cutoff, gain, sign, measured
  event age/jitter/loss, and stop reason;
- treat worsening and “no effect” as first-class results; and
- return to zero immediately on invalid state or user disable.

Never choose polarity merely because an animation “looks natural.” Optic-flow
direction depends on whether the object represents a world, horizon, particle
field, or vehicle-fixed reference. Validate each cue against its documented
perceptual model and synthetic direction fixtures.

## Calibration record

For reproducibility, store only non-personal configuration: software commit,
phone model/OS (when consent permits), mount-profile identifier, calibration ID,
sample rate, filters, cue parameters, and synthetic fixture version. Do not put
the pairing key, IP/MAC address, trip, location, participant identity, or health
information in repository fixtures.

Continue with [Safety](safety.md) and [Integration](integration.md).
