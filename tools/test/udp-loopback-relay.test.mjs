import assert from "node:assert/strict";
import test from "node:test";
import { isAllowedDatagram } from "../udp-loopback-relay.mjs";

test("UDP relay accepts only bounded datagrams from its pinned phone endpoint", () => {
  const expected = { family: "IPv4", address: "192.168.1.209", port: 28463 };
  assert.equal(isAllowedDatagram(Buffer.alloc(513), expected, expected.address, expected.port), true);
  assert.equal(isAllowedDatagram(Buffer.alloc(514), expected, expected.address, expected.port), false);
  assert.equal(isAllowedDatagram(Buffer.alloc(1), { ...expected, address: "192.168.1.210" }, expected.address, expected.port), false);
  assert.equal(isAllowedDatagram(Buffer.alloc(1), { ...expected, port: 28464 }, expected.address, expected.port), false);
  assert.equal(isAllowedDatagram(Buffer.alloc(0), expected, expected.address, expected.port), false);
});
