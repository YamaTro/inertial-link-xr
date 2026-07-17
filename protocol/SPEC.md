# InertialLink XR wire protocol 1.0

Status: **Research Preview, normative draft**
Protocol name: `ILXR`
Default UDP port: `28461`

This document specifies the authenticated local-datagram interface between a
vehicle-fixed motion sender and an XR application. It specifies transport and
validation only; it does not prescribe a visual cue or claim a comfort effect.

The key words **MUST**, **MUST NOT**, **REQUIRED**, **SHOULD**, **SHOULD NOT**,
and **MAY** are to be interpreted as described by BCP 14 when they appear in
uppercase.

## 1. Design constraints

- UDP unicast only. Implementations MUST NOT broadcast or multicast discovery.
- Maximum datagram size is 512 bytes. Larger datagrams MUST be discarded before
  allocation proportional to attacker-controlled lengths.
- Every multi-byte integer and IEEE-754 binary32 value is **big-endian**
  (network byte order).
- Every ILXR/1.0 datagram MUST be authenticated and header flag bit 0 MUST be
  set. A receiver MUST reject a packet with that bit clear, including on loopback.
- A datagram contains exactly one header, one payload, and one 16-byte tag.
  Trailing or missing bytes are invalid.

## 2. Pairing key and authentication

The pairing key is 16 cryptographically random bytes. User interfaces represent
it as 32 hexadecimal characters; parsers MAY ignore only ASCII space, tab, CR,
LF, and hyphen separators. They MUST reject Unicode whitespace, colons, every
other character, and any decoded length other than 16 bytes.

The Android reference sender generates a new key for every sender session,
including after a failed start or return from background. A key MUST NOT be
committed, logged, included in a recording, reused as a sample key, or
transmitted over the ILXR socket. Implementations SHOULD keep it only in memory
and erase the owned buffer when the session ends. Manual transfer of the key is
outside this protocol.

Header flag bit 0 is set and the sender appends:

```text
tag = first_16_bytes(HMAC-SHA-256(pairing_key, header || payload))
```

The tag is the first 16 bytes in digest order, not a hex string. Receivers MUST
compare tags in constant time before decoding motion fields. Truncation provides
128-bit tag strength; it does not encrypt the payload or hide traffic metadata.

## 3. Common header

The header is exactly 32 bytes.

| Offset | Size | Type | Field | Required value / meaning |
| ---: | ---: | --- | --- | --- |
| 0 | 4 | bytes | `magic` | ASCII `ILXR` (`49 4C 58 52`) |
| 4 | 1 | u8 | `major` | `1` |
| 5 | 1 | u8 | `minor` | `0` |
| 6 | 1 | u8 | `type` | `1` IMU, `2` sync request, `3` sync response |
| 7 | 1 | u8 | `flags` | exactly `1`: bit 0 authenticated; bits 1–7 zero |
| 8 | 2 | u16 | `headerLen` | `32` |
| 10 | 2 | u16 | `payloadLen` | message-specific length |
| 12 | 4 | u32 | `sequence` | unique, monotonically increasing in a session |
| 16 | 8 | u64 | `sessionId` | random non-zero sender-session identifier |
| 24 | 8 | i64 | `eventTimeNs` | monotonic nanoseconds in the originating clock |

Receivers MUST reject an unknown magic, major version, type, reserved flag, or
length. A 1.0 receiver SHOULD reject a higher minor version unless explicitly
updated to understand it. A future compatible receiver may accept an older
minor version according to that version's exact lengths.

The motion sender creates a cryptographically random non-zero `sessionId` for
each sender session. Each endpoint owns an independent outbound u32 sequence
counter. Within one `(authenticated source IP, source UDP port, sessionId)`
direction, sequence values MUST be unique and increase numerically without
wrapping. Sequence values from the opposite endpoint may coincide because the
authenticated source endpoint is different.

Before `0xFFFFFFFF` would advance to zero, its originator MUST stop that direction
or establish a new replay scope. The motion sender creates a fresh session ID; a
sync-requesting receiver can reopen from a new source endpoint or wait for a new
sender session. In the same scope, `0` after `0xFFFFFFFF` is old/replayed—not a
forward step. Receivers MUST scope replay state by source endpoint and
authenticated session—not by message type. A sequence previously accepted from
that source/session remains a replay even if it appears under a different type.
A bounded sliding window SHOULD allow lower-valued benign UDP reordering without
weakening duplicate rejection.

After accepting an authenticated IMU packet, a receiver MUST put that motion
sender's `sessionId` in each sync request. The sender rejects requests for any
other session. A sync response uses the same sender session. Receivers match the
authenticated endpoint, session, nonce, and `t0`; a receiver-generated session
identifier is not used on the wire. The sync request still uses the receiver's
own outbound sequence counter.

## 4. IMU sample (`type = 1`)

`payloadLen` is 80. An authenticated datagram is therefore 128 bytes:
32-byte header + 80-byte payload + 16-byte tag.

For IMU packets, `eventTimeNs` is the sensor event timestamp. On Android it is
nanoseconds since boot and shares the `SystemClock.elapsedRealtimeNanos()` time
base. It is not wall-clock time and MUST NOT be stored or interpreted as a date.

| Payload offset | Size | Type | Field | Unit / meaning |
| ---: | ---: | --- | --- | --- |
| 0 | 8 | i64 | `senderSendTimeNs` | sender monotonic time immediately before encoding/send |
| 8 | 12 | 3 × f32 | `rawAccel` | m/s²; Android accelerometer including gravity |
| 20 | 12 | 3 × f32 | `gyro` | rad/s angular velocity |
| 32 | 12 | 3 × f32 | `gravity` | m/s² estimated gravity vector |
| 44 | 12 | 3 × f32 | `linearAccel` | m/s² estimated acceleration with gravity removed |
| 56 | 16 | 4 × f32 | `rotation` | quaternion x, y, z, w |
| 72 | 4 | u32 | `calibrationId` | calibration generation, initially zero |
| 76 | 4 | u32 | `statusBits` | validity/calibration/accuracy flags below |

`calibrationId` increments after each successful stationary bias calibration.
It identifies a change; it is not a secret or globally unique. u32 wrap is
allowed and consumers compare it for inequality, not ordering.

### 4.1 Status bits

| Bit | Name | Meaning when set |
| ---: | --- | --- |
| 0 | `RAW_ACCEL_VALID` | `rawAccel` is available |
| 1 | `GYROSCOPE_VALID` | `gyro` is available |
| 2 | `GRAVITY_VALID` | `gravity` is available |
| 3 | `LINEAR_ACCEL_VALID` | `linearAccel` is available |
| 4 | `ROTATION_VALID` | `rotation` is available |
| 5 | `CALIBRATED` | stationary bias calibration completed |
| 6 | `CALIBRATING` | a calibration attempt is in progress |
| 8 | `ACCURACY_LOW` | worst raw-accel/gyro accuracy is low |
| 9 | `ACCURACY_MEDIUM` | worst accuracy is medium |
| 10 | `ACCURACY_HIGH` | worst accuracy is high |

Bits 7 and 11–31 are reserved and MUST be zero. At most one accuracy bit may be
set. No accuracy bit represents unreliable or unavailable accuracy. A validity
bit being clear means the corresponding field MUST NOT drive output, even if
its encoded floats happen to be zero.

### 4.2 Numeric validation

A receiver MUST authenticate and validate the full sample before publishing any
part of it. It MUST reject:

- any NaN or positive/negative infinity;
- `eventTimeNs` or `senderSendTimeNs` less than or equal to zero;
- any component of `rawAccel` or `linearAccel` with absolute value greater than
  200 m/s²;
- any `gravity` component with absolute value greater than 30 m/s²;
- any `gyro` component with absolute value greater than 50 rad/s;
- any quaternion component with absolute value greater than 1.5;
- a quaternion norm outside `[0.5, 1.5]`;
- an event time after send time.

After validation, receivers normalize a valid quaternion before use. These are
defensive wire bounds, not a statement that values inside them are physically
appropriate visual motion.

## 5. Clock synchronization

Sender and receiver monotonic clocks have unrelated origins. The receiver uses
an authenticated NTP-style exchange to estimate offset and delay.

### Sync request (`type = 2`)

Payload length is 16 bytes; authenticated total is 64 bytes.

| Offset | Size | Type | Field |
| ---: | ---: | --- | --- |
| 0 | 8 | i64 | `t0`, receiver monotonic send time in ns |
| 8 | 8 | u64 | random request `nonce` |

For a sync request, common-header `eventTimeNs` MUST equal payload `t0` exactly.
Encoders MUST NOT create and receivers MUST reject a request where they differ.

### Sync response (`type = 3`)

Payload length is 32 bytes; authenticated total is 80 bytes.

| Offset | Size | Type | Field |
| ---: | ---: | --- | --- |
| 0 | 8 | i64 | echoed `t0` |
| 8 | 8 | i64 | `t1`, sender monotonic receive time |
| 16 | 8 | i64 | `t2`, sender monotonic response-send time |
| 24 | 8 | u64 | echoed `nonce` |

The receiver records `t3` on receipt and accepts a response only when HMAC,
session, endpoint, nonce, and echoed `t0` match an outstanding request and
`t0 ≤ t3`, `t1 ≤ t2`. Then:

```text
sender_minus_receiver_offset = ((t1 - t0) + (t2 - t3)) / 2
round_trip_delay              = (t3 - t0) - (t2 - t1)
```

Calculations MUST use checked signed arithmetic wide enough to avoid overflow.
Receivers SHOULD prefer the lowest-delay recent sample and refresh periodically;
the reference receiver targets approximately two seconds, but cadence is not
normative. A response never bypasses IMU HMAC, replay, or numeric checks.

After mapping IMU `eventTimeNs` into receiver time, the reference safety policy
rejects samples older than 250 ms or more than 100 ms in the future. A receiver
without a trustworthy sync estimate MUST remain in `WarmingUp` and MUST NOT
drive content from apparently current data.

## 6. Coordinate system and mount transform

Every vector and quaternion is encoded in an OpenXR-style, right-handed vehicle
reference frame:

- +X points right;
- +Y points up;
- -Z points forward (+Z points rearward).

The reference Android mount is phone screen facing up, phone top edge facing
vehicle forward. It maps Android device vector `(x, y, z)` to wire vector
`(x, z, -y)`. A different physical mount MUST use an explicit proper-rotation
transform and a different calibration profile; consumers must never infer
mounting from incoming motion.

Quaternion components are ordered x, y, z, w. The Android sender prefers
`TYPE_GAME_ROTATION_VECTOR`, which provides gravity-referenced roll/pitch but an
arbitrary and drifting yaw, and falls back to `TYPE_ROTATION_VECTOR`. After the
mount basis is applied, the quaternion remains relative to Android's sensor
fusion reference. It is **not** geographic heading or a vehicle-world pose.
Consumers SHOULD establish a neutral baseline and use relative rotation; many
cues should prefer angular velocity and acceleration.

Unity uses a left-handed transform convention internally. Conversion MUST be
centralized in the protocol adapter and tested. Integrators MUST NOT independently
negate axes in scene scripts.

## 7. Replay, age, and dropout state

The reference receiver uses this externally observable safety state:

1. `Waiting`: no authenticated sender/session has been established. Output is
   neutral.
2. `WarmingUp`: an authenticated sender is known, but no output is allowed
   until clock sync and three consecutive valid IMU packets are accepted.
3. `Active`: valid data is current. The last valid output remains eligible for
   no more than 250 ms.
4. `Degraded`: after 250 ms without a valid packet, output fades linearly to
   neutral over the next 250 ms.
5. `FadedOut`: at 500 ms, output is neutral and remains neutral until recovery
   completes the warm-up requirement.

Invalid, unauthenticated, replayed, stale, or future packets MUST NOT refresh
the timer or interrupt a fade. A new session or calibration generation SHOULD
return the receiver to warm-up. Content drivers SHOULD expose the state so an
application can disable cues or show a non-motion status indicator.

## 8. Required parser order

To reduce parser and resource-exhaustion risk, receivers should perform checks
in this order:

1. receive one bounded UDP datagram (maximum 512 bytes);
2. check minimum length, magic, version, flags, header length, type, declared
   payload length, and exact total length;
3. look up the already-paired key without using payload data;
4. verify HMAC in constant time;
5. decode the fixed-length authenticated payload using checked reads into
   temporary values;
6. validate timestamps, status bits, finite numbers, ranges, and quaternion;
7. enforce endpoint/session and replay policy;
8. apply clock/staleness policy; then
9. atomically publish the complete sample.

No error response is sent for an invalid UDP packet. Implementations SHOULD
rate-limit aggregate diagnostics and MUST NOT log keys or raw packet contents.

## 9. Authentication is mandatory

ILXR/1.0 has no unauthenticated packet encoding. Header flag bit 0 clear is
invalid and a 16-byte tag is always present. Parser mutation tests feed malformed
buffers directly and do not need a network-accessible unauthenticated mode.

## 10. Compatibility and extension

Reserved fields and bits are zero in 1.0. New message types or changed lengths
require at least a minor protocol revision; incompatible header, authentication,
coordinate, or semantic changes require a major revision. Receivers must fail
closed on unknown semantics rather than guessing.

See [Threat model](../docs/threat-model.md), [Recording format](RECORDING.md),
and [Integration guide](../docs/integration.md).
