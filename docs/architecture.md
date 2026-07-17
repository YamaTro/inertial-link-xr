# Architecture

InertialLink XR separates four responsibilities so applications can adopt one
part without inheriting an opaque motion stack.

## Components

### Android protocol and motion source

`android/protocol-kotlin` owns byte layout, HMAC, keys, and message validation.
`android/motion-source-android` owns Android sensor registration, timestamp
alignment, fusion-source selection, mount transforms, and stationary bias
calibration. Neither module performs discovery or cloud communication.

`android/sender-app` is a minimal user-facing sender. It creates a random
in-memory pairing key and session ID for each sender session, rotates both after
a failed start or return from background, accepts an explicit receiver address,
sends authenticated unicast UDP samples, and responds to authenticated clock
sync requests. It requests no camera, GPS/location, or microphone permission.

### ILXR/1.0 transport

The [wire protocol](../protocol/SPEC.md) is a small fixed-layout datagram. Fixed
lengths, a 512-byte receive ceiling, HMAC-before-decode, replay tracking, and
strict numeric bounds keep attacker-controlled parsing narrow. The transport
does not decide how motion should appear in a scene.

### Unity receiver API

The Unity package `io.github.yamatro.inertiallink.xr` owns UDP reception, clock
sync, authentication, coordinate conversion, validity state, and publication
of the latest immutable safe sample through `VehicleMotionHub`. Network work
does not mutate Unity objects; scene code consumes a validated snapshot on the
main thread.

`EnvironmentMotionDriver` is an optional reference consumer. It applies bounded
motion only to an explicitly assigned `contentRoot`. At startup and whenever
configuration changes, it refuses a target whose ancestry or descendants would
move a Camera or XR Origin.

### Application-owned cue/content layer

An integrating application decides whether and how to use acceleration,
angular velocity, or relative rotation. It owns gain, filters, cue appearance,
accessibility, consent, and an immediate disable control. This is intentionally
outside the transport so researchers can compare approaches without forking
authentication and safety code.

## Data flow and trust boundaries

```text
Android sensors (trusted platform, noisy physical input)
  → motion source (mount transform, validity, calibration)
  → protocol encoder (untrusted network boundary, HMAC)
  → local unicast UDP (observable, lossy, reorderable, attacker-injectable)
  → bounded framing → HMAC → fixed-layout decode/numeric checks
  → endpoint/session/replay → clock/age
  → atomic safe MotionSample + ReceiverState
  → application cue policy
  → isolated contentRoot (never Camera/XR Origin)
```

No component treats an IP address as authentication. The pairing key authenticates
the datagram; endpoint pinning narrows the accepted peer after pairing. HMAC
does not encrypt motion values.

## Time model

Sensor time and sender-send time share Android's monotonic-since-boot clock.
Unity has a different monotonic origin. Authenticated sync messages estimate
the offset and network delay; wall clocks are never used. A receiver warms up
until it has a sync estimate and three valid samples, then rejects event ages
over 250 ms or more than 100 ms in the future.

Invalid input never refreshes liveness. After 250 ms without a valid sample,
the reference output fades to neutral for 250 ms and then remains neutral.

## Threading and allocation

- The socket reader uses a bounded reusable buffer.
- Parsing produces temporary values and publishes only a complete valid sample.
- HMAC and validation occur before Unity scene mutation.
- The Unity main thread reads the latest snapshot; it does not wait for UDP.
- Diagnostics are aggregated/rate-limited and never include keys or raw packets.

## Extension points

Applications should depend on the safe sample interface rather than the UDP
socket. Alternative transports, replay sources, and headset-only estimators can
implement the same producer contract. New wire fields require the compatibility
process in the specification; they must not be smuggled into reserved bits.
