# Protocol tools

These Node.js 24+ tools use only built-in modules and only synthetic data.

## Synthetic sender

```sh
node tools/synthetic-sender.mjs --key 00112233445566778899AABBCCDDEEFF --scenario gentle-turn --seconds 10
```

Scenarios are `stationary`, `gentle-turn`, and `gentle-brake`. The sender
responds to authenticated ILXR clock-sync requests, so it can exercise the full
Unity warm-up path. It does not read sensors or files.

## Packet inspector

```sh
node tools/packet-inspector.mjs --key 00112233445566778899AABBCCDDEEFF --count 60
```

The inspector checks exact lengths, HMAC, version, numeric bounds, status bits,
and replay before showing a compact summary. It never prints keys or raw packet
bytes.

Both network tools default to `127.0.0.1:28461`, require an explicit key, reject
DNS names, wildcard/multicast/broadcast destinations, and non-loopback addresses
without `--allow-network`. The synthetic sender uses a connected UDP socket and
also pins authenticated sync requests to the configured address and port.

`--key` exists only for public-vector loopback examples. A non-loopback host or
bind requires `ILXR_PAIRING_KEY`; the tool deletes that environment entry after
parsing and zeroes its owned key buffer on normal/error/signal exits where the
runtime permits. Prompt for the value instead of placing it in shell history,
never store it in `.env`, and clear it in the parent shell too. Environment
variables do not protect against a compromised same-user process. HMAC does not
encrypt traffic; read [SECURITY.md](../SECURITY.md) first.

## Recording validator

```sh
node tools/validate-recording.mjs recordings/*.ndjson
```

The validator rejects unknown metadata to reduce accidental inclusion of
personal data. It accepts repository-format synthetic fixtures, not UDP packet
captures.

## Tests

```sh
npm test
```

Kotlin, C#, and Node share [golden vectors](../protocol/TEST_VECTORS.md). Do not
treat a public vector key as a usable pairing secret.
