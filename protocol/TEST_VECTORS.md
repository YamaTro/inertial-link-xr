# ILXR/1.0 golden test vectors

These authenticated vectors are shared by the Kotlin, C#, and dependency-free
Node implementations. All bytes are hexadecimal, with no whitespace. Integer
and float fields are big-endian.

Pairing key for these **test vectors only**:

```text
000102030405060708090a0b0c0d0e0f
```

Never reuse this public value for a device or network session.

These are byte-encoding vectors, not physically coherent vehicle recordings.
Fields are intentionally independent: for example, the IMU vector uses a
non-zero `calibrationId` while leaving `CALIBRATED` clear so implementations do
not infer one field from the other. Runtime eligibility and repository recording
fixtures apply the stricter state rules described in `SPEC.md` and
`RECORDING.md`.

## IMU

- sequence `0x11223344`
- session `0x0102030405060708`
- event time `1000000000`
- send time `1000500000`
- raw acceleration `(1, 2, 3)`
- gyroscope `(0.1, 0.2, 0.3)`
- gravity `(0, -9.80665, 0)`
- linear acceleration `(1.1, 2.2, 3.3)`
- rotation `(0, 0, 0, 1)`
- calibration `0xAABBCCDD`
- status `0x0000041F`

```text
494c58520100010100200050112233440102030405060708000000003b9aca00000000003ba26b203f80000040000000404000003dcccccd3e4ccccd3e99999a00000000c11ce80a000000003f8ccccd400ccccd405333330000000000000000000000003f800000aabbccdd0000041fc469443bfeaa907111df804297ea6214
```

## Sync request

- sequence `1`
- sender session `0x8877665544332211`
- event time and `t0` `2000000000`
- nonce `0x1020304050607080`

```text
494c58520100020100200010000000018877665544332211000000007735940000000000773594001020304050607080a644347726832b2f8f879f74b9bf6a41
```

## Sync response

- sequence `2`
- same sender session and nonce
- event time and `t1` `2000050000`
- `t0` `2000000000`
- `t2` `2000075000`

```text
494c58520100030100200020000000028877665544332211000000007736575000000000773594000000000077365750000000007736b8f81020304050607080ccdad3a1c13f90a19a5b8c1cdf2e3baf
```

An implementation must also test invalid HMAC, reserved bits, multiple accuracy
bits, non-finite and out-of-bound floats, replay, timestamp errors, and every
incorrect length. Matching a happy-path vector alone is not conformance.
