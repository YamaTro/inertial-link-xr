import {
  createHmac,
  randomBytes,
  timingSafeEqual,
} from "node:crypto";

export const PROTOCOL = Object.freeze({
  magic: Buffer.from("ILXR", "ascii"),
  major: 1,
  minor: 0,
  headerLength: 32,
  authenticationTagLength: 16,
  maximumDatagramLength: 512,
  defaultPort: 28_461,
  type: Object.freeze({ imu: 1, syncRequest: 2, syncResponse: 3 }),
  flagAuthenticated: 1,
  payloadLength: Object.freeze({ 1: 80, 2: 16, 3: 32 }),
});

export const IMU_STATUS = Object.freeze({
  rawAccelValid: 1 << 0,
  gyroscopeValid: 1 << 1,
  gravityValid: 1 << 2,
  linearAccelValid: 1 << 3,
  rotationValid: 1 << 4,
  calibrated: 1 << 5,
  calibrating: 1 << 6,
  accuracyLow: 1 << 8,
  accuracyMedium: 1 << 9,
  accuracyHigh: 1 << 10,
  knownMask: 0x0000_077f,
  accuracyMask: 0x0000_0700,
});

const UINT32_MAX = 0xffff_ffff;
const UINT64_MAX = 0xffff_ffff_ffff_ffffn;
const INT64_MIN = -0x8000_0000_0000_0000n;
const INT64_MAX = 0x7fff_ffff_ffff_ffffn;

export class ProtocolError extends Error {
  constructor(message) {
    super(message);
    this.name = "ProtocolError";
  }
}

export function parsePairingKey(value) {
  if (Buffer.isBuffer(value) || value instanceof Uint8Array) {
    const key = Buffer.from(value);
    if (key.length !== 16) throw new ProtocolError("pairing key must be exactly 16 bytes");
    return key;
  }
  if (typeof value !== "string") throw new ProtocolError("pairing key is required");
  const compact = value.replace(/[ \t\r\n-]/g, "");
  if (!/^[0-9a-fA-F]{32}$/.test(compact)) {
    throw new ProtocolError("pairing key must contain exactly 32 hexadecimal characters");
  }
  return Buffer.from(compact, "hex");
}

export function generatePairingKey() {
  return randomBytes(16);
}

export function generateSessionId() {
  let value = 0n;
  while (value === 0n) value = randomBytes(8).readBigUInt64BE();
  return value;
}

function requireUInt32(name, value) {
  if (!Number.isInteger(value) || value < 0 || value > UINT32_MAX) {
    throw new ProtocolError(`${name} must be an unsigned 32-bit integer`);
  }
  return value;
}

function requireUInt64(name, value, nonzero = false) {
  if (typeof value === "number" && !Number.isSafeInteger(value)) {
    throw new ProtocolError(`${name} number input must be a safe integer; use bigint for full-width values`);
  }
  let result;
  try {
    result = BigInt(value);
  } catch {
    throw new ProtocolError(`${name} must be an unsigned 64-bit integer`);
  }
  if (result < 0n || result > UINT64_MAX || (nonzero && result === 0n)) {
    throw new ProtocolError(`${name} must be ${nonzero ? "a non-zero " : "an "}unsigned 64-bit integer`);
  }
  return result;
}

function requireInt64(name, value, positive = false) {
  if (typeof value === "number" && !Number.isSafeInteger(value)) {
    throw new ProtocolError(`${name} number input must be a safe integer; use bigint for full-width values`);
  }
  let result;
  try {
    result = BigInt(value);
  } catch {
    throw new ProtocolError(`${name} must be a signed 64-bit integer`);
  }
  if (result < INT64_MIN || result > INT64_MAX || (positive && result <= 0n)) {
    throw new ProtocolError(`${name} must be ${positive ? "a positive " : "a "}signed 64-bit integer`);
  }
  return result;
}

function requireFinite(name, value, maximum) {
  if (typeof value !== "number" || !Number.isFinite(value) || Math.abs(value) > maximum) {
    throw new ProtocolError(`${name} is non-finite or outside ±${maximum}`);
  }
  return value;
}

function requireVector(name, value, maximum, length = 3) {
  if (!Array.isArray(value) && !(value instanceof Float32Array)) {
    throw new ProtocolError(`${name} must be an array`);
  }
  if (value.length !== length) throw new ProtocolError(`${name} must contain ${length} components`);
  return Array.from(value, (component, index) => requireFinite(`${name}[${index}]`, component, maximum));
}

export function validateStatusBits(value) {
  const status = requireUInt32("statusBits", value);
  if ((status & ~IMU_STATUS.knownMask) !== 0) throw new ProtocolError("statusBits contains reserved bits");
  const accuracy = status & IMU_STATUS.accuracyMask;
  if (accuracy !== 0 && (accuracy & (accuracy - 1)) !== 0) {
    throw new ProtocolError("statusBits contains multiple accuracy levels");
  }
  return status;
}

export function validateImuPayload(value) {
  if (value === null || typeof value !== "object") throw new ProtocolError("IMU payload is required");
  const rotation = requireVector("rotation", value.rotation, 1.5, 4);
  const norm = Math.hypot(...rotation);
  if (norm < 0.5 || norm > 1.5) throw new ProtocolError("rotation quaternion norm is outside [0.5, 1.5]");
  return Object.freeze({
    senderSendTimeNs: requireInt64("senderSendTimeNs", value.senderSendTimeNs, true),
    rawAccel: Object.freeze(requireVector("rawAccel", value.rawAccel, 200)),
    gyro: Object.freeze(requireVector("gyro", value.gyro, 50)),
    gravity: Object.freeze(requireVector("gravity", value.gravity, 30)),
    linearAccel: Object.freeze(requireVector("linearAccel", value.linearAccel, 200)),
    rotation: Object.freeze(rotation),
    calibrationId: requireUInt32("calibrationId", value.calibrationId),
    statusBits: validateStatusBits(value.statusBits),
  });
}

function encodeHeader(type, sequence, sessionId, eventTimeNs, payloadLength) {
  const header = Buffer.alloc(PROTOCOL.headerLength);
  PROTOCOL.magic.copy(header, 0);
  header.writeUInt8(PROTOCOL.major, 4);
  header.writeUInt8(PROTOCOL.minor, 5);
  header.writeUInt8(type, 6);
  header.writeUInt8(PROTOCOL.flagAuthenticated, 7);
  header.writeUInt16BE(PROTOCOL.headerLength, 8);
  header.writeUInt16BE(payloadLength, 10);
  header.writeUInt32BE(requireUInt32("sequence", sequence), 12);
  header.writeBigUInt64BE(requireUInt64("sessionId", sessionId, true), 16);
  header.writeBigInt64BE(requireInt64("eventTimeNs", eventTimeNs, true), 24);
  return header;
}

function authenticate(header, payload, keyValue) {
  const body = Buffer.concat([header, payload]);
  return withCryptoKey(keyValue, (key) => {
    const digest = createHmac("sha256", key).update(body).digest();
    try {
      return Buffer.concat([body, digest.subarray(0, PROTOCOL.authenticationTagLength)]);
    } finally {
      digest.fill(0);
    }
  });
}

function withCryptoKey(value, operation) {
  let key;
  let owned = false;
  if (Buffer.isBuffer(value) || value instanceof Uint8Array) {
    if (value.byteLength !== 16) throw new ProtocolError("pairing key must be exactly 16 bytes");
    key = value;
  } else {
    key = parsePairingKey(value);
    owned = true;
  }
  try {
    return operation(key);
  } finally {
    if (owned) key.fill(0);
  }
}

function putVector(buffer, offset, vector) {
  for (let index = 0; index < vector.length; index += 1) {
    buffer.writeFloatBE(vector[index], offset + index * 4);
  }
}

export function encodeImuPacket({ sequence, sessionId, eventTimeNs, imu }, key) {
  const event = requireInt64("eventTimeNs", eventTimeNs, true);
  const value = validateImuPayload(imu);
  if (value.senderSendTimeNs < event) throw new ProtocolError("senderSendTimeNs must not precede eventTimeNs");
  const payload = Buffer.alloc(PROTOCOL.payloadLength[PROTOCOL.type.imu]);
  payload.writeBigInt64BE(value.senderSendTimeNs, 0);
  putVector(payload, 8, value.rawAccel);
  putVector(payload, 20, value.gyro);
  putVector(payload, 32, value.gravity);
  putVector(payload, 44, value.linearAccel);
  putVector(payload, 56, value.rotation);
  payload.writeUInt32BE(value.calibrationId, 72);
  payload.writeUInt32BE(value.statusBits, 76);
  return authenticate(encodeHeader(PROTOCOL.type.imu, sequence, sessionId, event, payload.length), payload, key);
}

export function encodeSyncRequest({ sequence, sessionId, eventTimeNs, t0, nonce }, key) {
  const event = requireInt64("eventTimeNs", eventTimeNs, true);
  const send = requireInt64("t0", t0, true);
  if (event !== send) throw new ProtocolError("sync request eventTimeNs must equal t0");
  const payload = Buffer.alloc(PROTOCOL.payloadLength[PROTOCOL.type.syncRequest]);
  payload.writeBigInt64BE(send, 0);
  payload.writeBigUInt64BE(requireUInt64("nonce", nonce), 8);
  return authenticate(encodeHeader(PROTOCOL.type.syncRequest, sequence, sessionId, event, payload.length), payload, key);
}

export function encodeSyncResponse({ sequence, sessionId, eventTimeNs, t0, t1, t2, nonce }, key) {
  const event = requireInt64("eventTimeNs", eventTimeNs, true);
  const first = requireInt64("t0", t0, true);
  const receive = requireInt64("t1", t1, true);
  const send = requireInt64("t2", t2, true);
  if (send < receive) throw new ProtocolError("t2 must not precede t1");
  const payload = Buffer.alloc(PROTOCOL.payloadLength[PROTOCOL.type.syncResponse]);
  payload.writeBigInt64BE(first, 0);
  payload.writeBigInt64BE(receive, 8);
  payload.writeBigInt64BE(send, 16);
  payload.writeBigUInt64BE(requireUInt64("nonce", nonce), 24);
  return authenticate(encodeHeader(PROTOCOL.type.syncResponse, sequence, sessionId, event, payload.length), payload, key);
}

function readVector(buffer, offset, length = 3) {
  return Object.freeze(Array.from({ length }, (_, index) => buffer.readFloatBE(offset + index * 4)));
}

export function decodePacket(datagramValue, keyValue) {
  if (!Buffer.isBuffer(datagramValue) && !(datagramValue instanceof Uint8Array)) {
    throw new ProtocolError("datagram must be a Buffer or Uint8Array");
  }
  if (datagramValue.byteLength > PROTOCOL.maximumDatagramLength) {
    throw new ProtocolError("datagram exceeds 512-byte maximum");
  }
  const datagram = Buffer.from(datagramValue);
  if (datagram.length < PROTOCOL.headerLength) throw new ProtocolError("datagram is shorter than the header");
  if (!datagram.subarray(0, 4).equals(PROTOCOL.magic)) throw new ProtocolError("invalid packet magic");
  const major = datagram.readUInt8(4);
  const minor = datagram.readUInt8(5);
  if (major !== PROTOCOL.major || minor !== PROTOCOL.minor) throw new ProtocolError(`unsupported protocol version ${major}.${minor}`);
  const type = datagram.readUInt8(6);
  const flags = datagram.readUInt8(7);
  if ((flags & ~PROTOCOL.flagAuthenticated) !== 0) throw new ProtocolError("packet contains reserved flags");
  const authenticated = (flags & PROTOCOL.flagAuthenticated) !== 0;
  if (!authenticated) throw new ProtocolError("packet authentication is required");
  const headerLength = datagram.readUInt16BE(8);
  const payloadLength = datagram.readUInt16BE(10);
  if (headerLength !== PROTOCOL.headerLength) throw new ProtocolError("invalid header length");
  if (!(type in PROTOCOL.payloadLength) || payloadLength !== PROTOCOL.payloadLength[type]) {
    throw new ProtocolError("unknown type or invalid payload length");
  }
  const bodyLength = headerLength + payloadLength;
  const expectedLength = bodyLength + (authenticated ? PROTOCOL.authenticationTagLength : 0);
  if (datagram.length !== expectedLength) throw new ProtocolError("datagram has trailing or missing bytes");
  if (authenticated) {
    withCryptoKey(keyValue, (key) => {
      const digest = createHmac("sha256", key).update(datagram.subarray(0, bodyLength)).digest();
      try {
        const expected = digest.subarray(0, PROTOCOL.authenticationTagLength);
        const received = datagram.subarray(bodyLength);
        if (received.length !== expected.length || !timingSafeEqual(received, expected)) {
          throw new ProtocolError("packet authentication failed");
        }
      } finally {
        digest.fill(0);
      }
    });
  }
  const header = Object.freeze({
    major,
    minor,
    type,
    authenticated,
    sequence: datagram.readUInt32BE(12),
    sessionId: datagram.readBigUInt64BE(16),
    eventTimeNs: requireInt64("eventTimeNs", datagram.readBigInt64BE(24), true),
  });
  const offset = headerLength;
  let payload;
  if (type === PROTOCOL.type.imu) {
    payload = validateImuPayload({
      senderSendTimeNs: datagram.readBigInt64BE(offset),
      rawAccel: readVector(datagram, offset + 8),
      gyro: readVector(datagram, offset + 20),
      gravity: readVector(datagram, offset + 32),
      linearAccel: readVector(datagram, offset + 44),
      rotation: readVector(datagram, offset + 56, 4),
      calibrationId: datagram.readUInt32BE(offset + 72),
      statusBits: datagram.readUInt32BE(offset + 76),
    });
    if (payload.senderSendTimeNs < header.eventTimeNs) {
      throw new ProtocolError("senderSendTimeNs must not precede eventTimeNs");
    }
  } else if (type === PROTOCOL.type.syncRequest) {
    const t0 = requireInt64("t0", datagram.readBigInt64BE(offset), true);
    if (t0 !== header.eventTimeNs) throw new ProtocolError("sync request eventTimeNs must equal t0");
    payload = Object.freeze({ t0, nonce: datagram.readBigUInt64BE(offset + 8) });
  } else {
    const t0 = requireInt64("t0", datagram.readBigInt64BE(offset), true);
    const t1 = requireInt64("t1", datagram.readBigInt64BE(offset + 8), true);
    const t2 = requireInt64("t2", datagram.readBigInt64BE(offset + 16), true);
    if (t2 < t1) throw new ProtocolError("t2 must not precede t1");
    payload = Object.freeze({ t0, t1, t2, nonce: datagram.readBigUInt64BE(offset + 24) });
  }
  return Object.freeze({ header, payload });
}

export class ReplayWindow {
  #sessions = new Map();
  #windowSize;
  #maximumSessions;

  constructor({ windowSize = 64, maximumSessions = 8 } = {}) {
    if (!Number.isInteger(windowSize) || windowSize < 1 || windowSize > 64) throw new RangeError("windowSize must be 1..64");
    if (!Number.isInteger(maximumSessions) || maximumSessions < 1 || maximumSessions > 64) throw new RangeError("maximumSessions must be 1..64");
    this.#windowSize = windowSize;
    this.#maximumSessions = maximumSessions;
  }

  accept(sessionIdValue, sequenceValue, endpoint = "") {
    const sessionId = requireUInt64("sessionId", sessionIdValue, true);
    const sequence = requireUInt32("sequence", sequenceValue);
    const key = `${endpoint}|${sessionId.toString(16)}`;
    let state = this.#sessions.get(key);
    if (!state) {
      if (this.#sessions.size >= this.#maximumSessions) this.#sessions.delete(this.#sessions.keys().next().value);
      this.#sessions.set(key, { maximum: sequence, bitmap: 1n });
      return true;
    }
    this.#sessions.delete(key);
    this.#sessions.set(key, state);
    if (sequence > state.maximum) {
      const shift = sequence - state.maximum;
      state.bitmap = shift >= this.#windowSize
        ? 1n
        : ((state.bitmap << BigInt(shift)) | 1n) & ((1n << BigInt(this.#windowSize)) - 1n);
      state.maximum = sequence;
      return true;
    }
    const distance = state.maximum - sequence;
    if (distance >= this.#windowSize) return false;
    const bit = 1n << BigInt(distance);
    if ((state.bitmap & bit) !== 0n) return false;
    state.bitmap |= bit;
    return true;
  }

  clear() {
    this.#sessions.clear();
  }
}

export function formatSessionId(value) {
  return `0x${requireUInt64("sessionId", value).toString(16).padStart(16, "0")}`;
}
