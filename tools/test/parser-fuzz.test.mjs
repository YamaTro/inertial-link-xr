import assert from "node:assert/strict";
import test from "node:test";
import { decodePacket } from "../lib/ilxr.mjs";

const KEY_HEX = "000102030405060708090a0b0c0d0e0f";
const GOLDEN_IMU = Buffer.from(
  "494c58520100010100200050112233440102030405060708000000003b9aca00" +
  "000000003ba26b203f80000040000000404000003dcccccd3e4ccccd3e99999a" +
  "00000000c11ce80a000000003f8ccccd400ccccd405333330000000000000000" +
  "000000003f800000aabbccdd0000041fc469443bfeaa907111df804297ea6214",
  "hex",
);

function deterministicWords(seed = 0x51_4c_58_52) {
  let state = seed >>> 0;
  return () => {
    state = (Math.imul(state, 1_664_525) + 1_013_904_223) >>> 0;
    return state;
  };
}

test("bounded deterministic parser mutations fail closed without crashes", () => {
  const next = deterministicWords();

  for (let length = 0; length < GOLDEN_IMU.length; length += 1) {
    assert.throws(() => decodePacket(GOLDEN_IMU.subarray(0, length), KEY_HEX));
  }

  for (let iteration = 0; iteration < 1_024; iteration += 1) {
    const length = next() % 513;
    const candidate = Buffer.alloc(length);
    for (let offset = 0; offset < length; offset += 1) candidate[offset] = next() & 0xff;
    assert.throws(() => decodePacket(candidate, KEY_HEX));
  }

  for (let iteration = 0; iteration < 512; iteration += 1) {
    const candidate = Buffer.from(GOLDEN_IMU);
    const offset = next() % candidate.length;
    candidate[offset] ^= 1 + (next() % 255);
    assert.throws(() => decodePacket(candidate, KEY_HEX));
  }

  assert.throws(() => decodePacket(Buffer.alloc(513), KEY_HEX), /512-byte maximum/);
});
