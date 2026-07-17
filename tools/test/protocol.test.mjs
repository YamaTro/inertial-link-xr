import assert from "node:assert/strict";
import { createHmac } from "node:crypto";
import test from "node:test";
import {
  PROTOCOL,
  ProtocolError,
  ReplayWindow,
  decodePacket,
  encodeImuPacket,
  encodeSyncRequest,
  encodeSyncResponse,
  parsePairingKey,
} from "../lib/ilxr.mjs";
import {
  assertAddressAllowed,
  isExpectedRemote,
  normalizeIpAddress,
  parseArguments,
} from "../lib/cli.mjs";
import { consumePairingKey } from "../lib/key-input.mjs";
import { SyncRequestGuard } from "../lib/sync-request-guard.mjs";

const KEY_HEX = "000102030405060708090a0b0c0d0e0f";
const GOLDEN_IMU = "494c58520100010100200050112233440102030405060708000000003b9aca00000000003ba26b203f80000040000000404000003dcccccd3e4ccccd3e99999a00000000c11ce80a000000003f8ccccd400ccccd405333330000000000000000000000003f800000aabbccdd0000041fc469443bfeaa907111df804297ea6214";
const GOLDEN_SYNC_REQUEST = "494c58520100020100200010000000018877665544332211000000007735940000000000773594001020304050607080a644347726832b2f8f879f74b9bf6a41";
const GOLDEN_SYNC_RESPONSE = "494c58520100030100200020000000028877665544332211000000007736575000000000773594000000000077365750000000007736b8f81020304050607080ccdad3a1c13f90a19a5b8c1cdf2e3baf";

function goldenImu() {
  return encodeImuPacket({
    sequence: 0x11223344,
    sessionId: 0x0102030405060708n,
    eventTimeNs: 1_000_000_000n,
    imu: {
      senderSendTimeNs: 1_000_500_000n,
      rawAccel: [1, 2, 3],
      gyro: [0.1, 0.2, 0.3],
      gravity: [0, -9.80665, 0],
      linearAccel: [1.1, 2.2, 3.3],
      rotation: [0, 0, 0, 1],
      calibrationId: 0xaabbccdd,
      statusBits: 0x0000041f,
    },
  }, KEY_HEX);
}

test("pairing keys accept documented separators but require exactly 16 bytes", () => {
  assert.equal(parsePairingKey("0001-0203 0405-0607 0809-0A0B 0C0D-0E0F").toString("hex"), KEY_HEX);
  assert.throws(() => parsePairingKey("00"), ProtocolError);
  assert.throws(() => parsePairingKey("000102030405060708090a0b0c0d0e0g"), ProtocolError);
  assert.throws(() => parsePairingKey("0001020304050607\u00a008090a0b0c0d0e0f"), ProtocolError);
  assert.throws(() => parsePairingKey("00:01:02:03:04:05:06:07:08:09:0a:0b:0c:0d:0e:0f"), ProtocolError);
});

test("pairing key input removes the environment copy and protects non-loopback use", () => {
  const environment = { ILXR_PAIRING_KEY: KEY_HEX };
  const key = consumePairingKey({ environment, requireEnvironment: true });
  assert.equal(key.toString("hex"), KEY_HEX);
  assert.equal(Object.hasOwn(environment, "ILXR_PAIRING_KEY"), false);
  key.fill(0);

  const conflicting = { ILXR_PAIRING_KEY: KEY_HEX };
  assert.throws(
    () => consumePairingKey({ argumentValue: KEY_HEX, environment: conflicting }),
    /either --key or ILXR_PAIRING_KEY/,
  );
  assert.equal(Object.hasOwn(conflicting, "ILXR_PAIRING_KEY"), false);
  assert.throws(
    () => consumePairingKey({ argumentValue: KEY_HEX, environment: {}, requireEnvironment: true }),
    /--key is loopback-only/,
  );
});

test("IMU encoder matches the shared Kotlin/C# golden vector", () => {
  assert.equal(goldenImu().toString("hex"), GOLDEN_IMU);
  const decoded = decodePacket(Buffer.from(GOLDEN_IMU, "hex"), KEY_HEX);
  assert.equal(decoded.header.sequence, 0x11223344);
  assert.equal(decoded.header.sessionId, 0x0102030405060708n);
  assert.equal(decoded.payload.statusBits, 0x41f);
  assert.deepEqual(decoded.payload.rawAccel, [1, 2, 3]);
});

test("sync messages match shared golden vectors and sender session", () => {
  const request = encodeSyncRequest({
    sequence: 1,
    sessionId: 0x8877665544332211n,
    eventTimeNs: 2_000_000_000n,
    t0: 2_000_000_000n,
    nonce: 0x1020304050607080n,
  }, KEY_HEX);
  assert.equal(request.toString("hex"), GOLDEN_SYNC_REQUEST);
  const response = encodeSyncResponse({
    sequence: 2,
    sessionId: 0x8877665544332211n,
    eventTimeNs: 2_000_050_000n,
    t0: 2_000_000_000n,
    t1: 2_000_050_000n,
    t2: 2_000_075_000n,
    nonce: 0x1020304050607080n,
  }, KEY_HEX);
  assert.equal(response.toString("hex"), GOLDEN_SYNC_RESPONSE);
  assert.equal(decodePacket(request, KEY_HEX).header.sessionId, decodePacket(response, KEY_HEX).header.sessionId);
});

test("sync requests require the header event time to equal t0", () => {
  assert.throws(() => encodeSyncRequest({
    sequence: 1,
    sessionId: 1n,
    eventTimeNs: 10n,
    t0: 11n,
    nonce: 1n,
  }, KEY_HEX), /eventTimeNs must equal t0/);

  const mismatched = Buffer.from(GOLDEN_SYNC_REQUEST, "hex");
  mismatched.writeBigInt64BE(2_000_000_001n, PROTOCOL.headerLength);
  const bodyLength = PROTOCOL.headerLength + PROTOCOL.payloadLength[PROTOCOL.type.syncRequest];
  createHmac("sha256", Buffer.from(KEY_HEX, "hex"))
    .update(mismatched.subarray(0, bodyLength))
    .digest()
    .subarray(0, PROTOCOL.authenticationTagLength)
    .copy(mismatched, bodyLength);
  assert.throws(() => decodePacket(mismatched, KEY_HEX), /eventTimeNs must equal t0/);
});

test("tampering and the wrong key fail authentication", () => {
  const packet = goldenImu();
  packet[60] ^= 0x01;
  assert.throws(() => decodePacket(packet, KEY_HEX), /authentication failed/);
  assert.throws(() => decodePacket(goldenImu(), "ffffffffffffffffffffffffffffffff"), /authentication failed/);
});

test("unauthenticated packets are always rejected", () => {
  const packet = goldenImu();
  packet[7] = 0;
  const withoutTag = packet.subarray(0, packet.length - PROTOCOL.authenticationTagLength);
  assert.throws(() => decodePacket(withoutTag, KEY_HEX), /authentication is required/);
});

test("authenticated reserved status bits are rejected after HMAC", () => {
  const packet = goldenImu();
  packet.writeUInt32BE(0x8000041f, 108);
  const bodyLength = PROTOCOL.headerLength + PROTOCOL.payloadLength[PROTOCOL.type.imu];
  const tag = createHmac("sha256", Buffer.from(KEY_HEX, "hex"))
    .update(packet.subarray(0, bodyLength))
    .digest()
    .subarray(0, 16);
  tag.copy(packet, bodyLength);
  assert.throws(() => decodePacket(packet, KEY_HEX), /reserved bits/);
});

test("multiple accuracy bits and unsafe numeric values are rejected", () => {
  const base = {
    sequence: 1,
    sessionId: 1n,
    eventTimeNs: 1n,
    imu: {
      senderSendTimeNs: 2n,
      rawAccel: [0, 9.8, 0],
      gyro: [0, 0, 0],
      gravity: [0, 9.8, 0],
      linearAccel: [0, 0, 0],
      rotation: [0, 0, 0, 1],
      calibrationId: 0,
      statusBits: 0x300,
    },
  };
  assert.throws(() => encodeImuPacket(base, KEY_HEX), /multiple accuracy/);
  assert.throws(
    () => encodeImuPacket({ ...base, imu: { ...base.imu, statusBits: 0x400, gravity: [0, 31, 0] } }, KEY_HEX),
    /outside/,
  );
  assert.throws(
    () => encodeImuPacket({ ...base, imu: { ...base.imu, statusBits: 0x400, gyro: [Number.NaN, 0, 0] } }, KEY_HEX),
    /non-finite/,
  );
});

test("replay window accepts bounded reordering once and rejects duplicates/old packets", () => {
  const replay = new ReplayWindow({ windowSize: 4 });
  assert.equal(replay.accept(7n, 10, "peer"), true);
  assert.equal(replay.accept(7n, 12, "peer"), true);
  assert.equal(replay.accept(7n, 11, "peer"), true);
  assert.equal(replay.accept(7n, 11, "peer"), false);
  assert.equal(replay.accept(7n, 8, "peer"), false);
  assert.equal(replay.accept(8n, 1, "peer"), true);
});

test("replay window rejects same-session uint32 wrap", () => {
  const replay = new ReplayWindow({ windowSize: 4 });
  assert.equal(replay.accept(7n, 0xffff_fffe, "peer"), true);
  assert.equal(replay.accept(7n, 0xffff_ffff, "peer"), true);
  assert.equal(replay.accept(7n, 0, "peer"), false);
  assert.equal(replay.accept(7n, 0xffff_ffff, "peer"), false);
  assert.equal(replay.accept(8n, 0, "peer"), true);
});

test("replay scope is source endpoint plus session, never message type", () => {
  const replay = new ReplayWindow();
  const sessionId = 0x0102030405060708n;
  const sequence = 17;
  const endpoint = "127.0.0.1:28461";
  const imu = decodePacket(encodeImuPacket({
    sequence,
    sessionId,
    eventTimeNs: 10n,
    imu: {
      senderSendTimeNs: 11n,
      rawAccel: [0, 9.8, 0],
      gyro: [0, 0, 0],
      gravity: [0, 9.8, 0],
      linearAccel: [0, 0, 0],
      rotation: [0, 0, 0, 1],
      calibrationId: 1,
      statusBits: 0x43f,
    },
  }, KEY_HEX), KEY_HEX);
  const sync = decodePacket(encodeSyncRequest({
    sequence,
    sessionId,
    eventTimeNs: 12n,
    t0: 12n,
    nonce: 9n,
  }, KEY_HEX), KEY_HEX);
  assert.notEqual(imu.header.type, sync.header.type);
  assert.equal(replay.accept(imu.header.sessionId, imu.header.sequence, endpoint), true);
  assert.equal(replay.accept(sync.header.sessionId, sync.header.sequence, endpoint), false);
  assert.equal(replay.accept(sync.header.sessionId, sync.header.sequence, "127.0.0.1:30000"), true);
});

test("CLI rejects unknown/duplicate options and normalizes expected sync peer", () => {
  assert.throws(() => parseArguments(["--surprise", "yes"], ["key"]), /unknown option/);
  assert.throws(() => parseArguments(["--key", "a", "--key", "b"], ["key"]), /duplicate option/);
  assert.equal(normalizeIpAddress("::ffff:127.0.0.1"), "127.0.0.1");
  assert.equal(isExpectedRemote({ address: "::ffff:127.0.0.1", port: 28_461 }, "127.0.0.1", 28_461), true);
  assert.equal(isExpectedRemote({ address: "127.0.0.1", port: 28_462 }, "127.0.0.1", 28_461), false);
  assert.equal(isExpectedRemote({ address: "127.0.0.2", port: 28_461 }, "127.0.0.1", 28_461), false);
});

test("network address policy rejects unspecified, multicast, and broadcast literals", () => {
  for (const address of ["0.0.0.0", "224.0.0.1", "239.255.255.250", "192.168.1.255", "255.255.255.255", "::", "ff02::1"]) {
    assert.throws(() => assertAddressAllowed(address, true), /not allowed/);
  }
  assert.doesNotThrow(() => assertAddressAllowed("127.0.0.1", false));
  assert.doesNotThrow(() => assertAddressAllowed("192.168.1.10", true));
});

test("synthetic sync guard pins peer and rejects malformed direction before replay", () => {
  const sessionId = 5n;
  const remote = { address: "127.0.0.1", port: PROTOCOL.defaultPort };
  const guard = new SyncRequestGuard({
    expectedHost: "127.0.0.1",
    expectedPort: PROTOCOL.defaultPort,
    sessionId,
  });
  const request = decodePacket(encodeSyncRequest({
    sequence: 7,
    sessionId,
    eventTimeNs: 100n,
    t0: 100n,
    nonce: 1n,
  }, KEY_HEX), KEY_HEX);
  assert.equal(guard.accept(request, { ...remote, port: PROTOCOL.defaultPort + 1 }), false);
  assert.equal(guard.accept(request, remote), true);

  const sameSequenceFreshNonce = decodePacket(encodeSyncRequest({
    sequence: 7,
    sessionId,
    eventTimeNs: 101n,
    t0: 101n,
    nonce: 2n,
  }, KEY_HEX), KEY_HEX);
  assert.equal(guard.accept(sameSequenceFreshNonce, remote), false);

  const crossTypeGuard = new SyncRequestGuard({
    expectedHost: "127.0.0.1",
    expectedPort: PROTOCOL.defaultPort,
    sessionId,
  });
  const imu = decodePacket(encodeImuPacket({
    sequence: 9,
    sessionId,
    eventTimeNs: 200n,
    imu: {
      senderSendTimeNs: 201n,
      rawAccel: [0, 9.8, 0], gyro: [0, 0, 0], gravity: [0, 9.8, 0],
      linearAccel: [0, 0, 0], rotation: [0, 0, 0, 1], calibrationId: 1, statusBits: 0x43f,
    },
  }, KEY_HEX), KEY_HEX);
  assert.equal(crossTypeGuard.accept(imu, remote), false);
  const sameSequenceRequest = decodePacket(encodeSyncRequest({
    sequence: 9,
    sessionId,
    eventTimeNs: 202n,
    t0: 202n,
    nonce: 3n,
  }, KEY_HEX), KEY_HEX);
  assert.equal(crossTypeGuard.accept(sameSequenceRequest, remote), true);

  const malformedGuard = new SyncRequestGuard({
    expectedHost: "127.0.0.1",
    expectedPort: PROTOCOL.defaultPort,
    sessionId,
  });
  assert.equal(malformedGuard.accept({
    header: { type: PROTOCOL.type.syncRequest, sessionId, sequence: 12, eventTimeNs: 300n },
    payload: { t0: 301n, nonce: 1n },
  }, remote), false);
  const validAfterMalformed = decodePacket(encodeSyncRequest({
    sequence: 12,
    sessionId,
    eventTimeNs: 300n,
    t0: 300n,
    nonce: 1n,
  }, KEY_HEX), KEY_HEX);
  assert.equal(malformedGuard.accept(validAfterMalformed, remote), true);
});

test("synthetic sync guard bounds its recent nonce cache", () => {
  const sessionId = 6n;
  const remote = { address: "127.0.0.1", port: PROTOCOL.defaultPort };
  const guard = new SyncRequestGuard({
    expectedHost: remote.address,
    expectedPort: remote.port,
    sessionId,
  });
  for (let value = 0; value < 65; value += 1) {
    const time = BigInt(1_000 + value);
    const request = decodePacket(encodeSyncRequest({
      sequence: value,
      sessionId,
      eventTimeNs: time,
      t0: time,
      nonce: BigInt(value),
    }, KEY_HEX), KEY_HEX);
    assert.equal(guard.accept(request, remote), true);
  }
  const evictedNonce = decodePacket(encodeSyncRequest({
    sequence: 65,
    sessionId,
    eventTimeNs: 2_000n,
    t0: 2_000n,
    nonce: 0n,
  }, KEY_HEX), KEY_HEX);
  assert.equal(guard.accept(evictedNonce, remote), true);
});

test("parser rejects trailing data, invalid timestamps, and impossible sync order", () => {
  assert.throws(() => decodePacket(Buffer.concat([goldenImu(), Buffer.of(0)]), KEY_HEX), /trailing or missing/);
  assert.throws(
    () => encodeSyncResponse({
      sequence: 1,
      sessionId: 1n,
      eventTimeNs: 1n,
      t0: 1n,
      t1: 3n,
      t2: 2n,
      nonce: 1n,
    }, KEY_HEX),
    /t2 must not precede t1/,
  );
});

test("parser caps input before copying and encoders reject unsafe Number integers", () => {
  assert.throws(() => decodePacket(Buffer.alloc(10 * 1024 * 1024), KEY_HEX), /512-byte maximum/);
  assert.throws(() => decodePacket("not bytes", KEY_HEX), /Buffer or Uint8Array/);
  assert.throws(() => encodeSyncRequest({
    sequence: 1,
    sessionId: Number.MAX_SAFE_INTEGER + 1,
    eventTimeNs: 1n,
    t0: 1n,
    nonce: 1n,
  }, KEY_HEX), /safe integer/);
  assert.throws(() => encodeSyncRequest({
    sequence: 1,
    sessionId: 1n,
    eventTimeNs: Number.MAX_SAFE_INTEGER + 1,
    t0: Number.MAX_SAFE_INTEGER + 1,
    nonce: 1n,
  }, KEY_HEX), /safe integer/);
});
