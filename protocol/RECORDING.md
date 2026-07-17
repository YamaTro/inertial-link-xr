# Synthetic recording format 1.0

Repository recordings are newline-delimited JSON (NDJSON) fixtures for offline
tests. They are not the UDP wire format and contain no authentication key.

The first non-empty line is metadata:

```json
{"kind":"ilxr-recording","version":1,"synthetic":true,"units":"SI","coordinates":"OpenXR RH +X right +Y up -Z forward","generator":"named deterministic generator"}
```

Every following line is a sample:

```json
{"kind":"sample","tNs":"0","rawAccel":[0,9.80665,0],"gyro":[0,0,0],"gravity":[0,9.80665,0],"linearAccel":[0,0,0],"rotation":[0,0,0,1],"calibrationId":1,"statusBits":1087}
```

Rules:

- `synthetic` MUST be literal `true` for files committed under `recordings/`.
- `generator` MUST describe a deterministic synthetic process and MUST NOT name
  a person, device identifier, route, or location.
- `tNs` is a base-10 string containing a non-negative, strictly increasing
  relative time. It is not a wall-clock timestamp.
- Vector, quaternion, calibration, status, finite-number, and range semantics
  match [`SPEC.md`](SPEC.md).
- Committed synthetic samples with a non-zero `calibrationId` MUST set
  `CALIBRATED`; `CALIBRATED` and `CALIBRATING` MUST NOT both be set. This is a
  repository-fixture rule, not an additional wire-protocol invariant.
- Metadata, pairing keys, IP addresses, device identifiers, GPS, health reports,
  and free-form notes are prohibited.
- Unknown fields are rejected by the repository validator to prevent accidental
  personal metadata from being committed.

Run `node tools/validate-recording.mjs recordings/*.ndjson` before committing.
The validator is a schema and safety check, not proof that a fixture represents
a real vehicle.
