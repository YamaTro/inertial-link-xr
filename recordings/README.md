# Recordings

Only deterministic synthetic NDJSON fixtures belong in this directory. The
format is specified in [`protocol/RECORDING.md`](../protocol/RECORDING.md).

Do not commit real trips, passenger data, wall-clock times, locations, IP/MAC
addresses, device identifiers, pairing keys, symptoms, health information, or
packet captures. Keep local experiments under ignored `recordings/local/` and
apply an appropriate consent, retention, and deletion policy outside this
repository.

Validate committed fixtures with:

```sh
npm run validate:recordings
```

Passing validation proves only that the file has the strict synthetic fixture
shape and safe numeric bounds; it does not establish physical realism or a
comfort effect.
