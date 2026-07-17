# Limitations

## Platform and integration

- This is an application/SDK integration, not a Quest or OpenXR system overlay.
  It cannot alter arbitrary existing apps.
- Unity 2022.3 LTS+ is the reference client. Other engines can implement the
  public protocol but are not yet compatibility-tested.
- The driver acts on an isolated content root. Moving an entire XR Origin or
  Camera would mix vehicle and head motion and is deliberately refused.
- Headset tracking behavior inside buses/cars varies by device, firmware,
  lighting, cabin geometry, and travel-mode support. The protocol cannot repair
  lost or incorrect platform tracking.

## Sensing and estimation

- Phone accelerometers measure specific force including gravity. Gravity and
  linear-acceleration fields are sensor-fusion estimates, not ground truth.
- IMU-only data cannot provide stable long-term position. Integrating
  acceleration accumulates bias rapidly; this project is not inertial navigation.
- `TYPE_GAME_ROTATION_VECTOR` yaw is arbitrary and can drift. Fallback rotation
  vectors may use different fusion inputs. Neither is geographic vehicle heading.
- Sensor rate, batching, timestamp quality, saturation, bias, and heat/power
  behavior differ across Android devices.
- A loose, flexible, misoriented, or vibration-resonant phone mount measures
  something different from intended vehicle-body motion.
- The default transform covers one explicit mount only. Automatic mount
  inference is intentionally absent.

## Transport and timing

- UDP can lose, duplicate, reorder, or delay packets. HMAC does not make UDP
  reliable and does not encrypt it.
- Clock synchronization estimates delay; asymmetric network latency cannot be
  perfectly separated from clock offset.
- The 250 ms stale and 500 ms neutral-output bounds are defensive defaults, not
  evidence that all lower latencies are comfortable.
- Busy phones, Wi-Fi power saving, OS background restrictions, and Unity frame
  timing can add latency not visible in a sender-only metric.
- Android host apps targeting SDK 37+ can lose LAN UDP when the user denies or
  revokes `ACCESS_LOCAL_NETWORK`; integrations must fail closed and fade neutral.
  The v0.1 sender targets SDK 35 and correctly relies on `INTERNET` alone.

## Human factors and evidence

- The project has not performed a clinical trial and makes no individual or
  population efficacy claim.
- Prior vehicle-XR cue studies are generally small and condition-specific.
  Cue type, sign, amplitude, latency, field of view, task, route, and participant
  susceptibility can change outcomes.
- “More physically accurate” does not necessarily mean more comfortable. A
  sign or phase error can plausibly worsen sensory conflict.
- Short tests cannot establish long-duration safety, habituation, accessibility,
  or effects in a broad population.

## Security and privacy

- Authentication detects forged/modified packets from parties without the key;
  it does not conceal motion or traffic timing.
- A compromised/rooted phone, compromised XR app, stolen pairing key, or custom
  integration that bypasses guards is outside the protocol's protection.
- The repository includes synthetic recordings only. The software cannot make
  a separately collected human-subject dataset ethical or anonymous.

See [Safety](safety.md) and [Threat model](threat-model.md) before integration.
