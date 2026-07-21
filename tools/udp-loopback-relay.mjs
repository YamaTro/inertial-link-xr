#!/usr/bin/env node
import dgram from "node:dgram";
import net from "node:net";

const MAX_DATAGRAM_SIZE = 513;

function readArguments(argv) {
  const allowed = new Set(["--phone", "--phone-port", "--listen-port", "--unity-port"]);
  const values = new Map();
  for (let index = 0; index < argv.length; index += 2) {
    const name = argv[index];
    const value = argv[index + 1];
    if (!allowed.has(name) || value === undefined || values.has(name)) {
      throw new Error("Expected unique --phone, --phone-port, --listen-port, and --unity-port options");
    }
    values.set(name, value);
  }
  if (values.size !== allowed.size) throw new Error("All relay options are required");
  const phone = values.get("--phone");
  if (!net.isIPv4(phone) || !isPrivateIpv4(phone)) throw new Error("Phone must be a private IPv4 literal");
  return {
    phone,
    phonePort: parsePort(values.get("--phone-port")),
    listenPort: parsePort(values.get("--listen-port")),
    unityPort: parsePort(values.get("--unity-port")),
  };
}

function parsePort(value) {
  if (!/^[0-9]{4,5}$/.test(value)) throw new Error("Ports must be numeric and unprivileged");
  const port = Number(value);
  if (!Number.isSafeInteger(port) || port < 1024 || port > 65535) throw new Error("Port is outside 1024..65535");
  return port;
}

function isPrivateIpv4(value) {
  const [first, second] = value.split(".").map(Number);
  return first === 10 || (first === 172 && second >= 16 && second <= 31) || (first === 192 && second === 168);
}

export function isAllowedDatagram(message, remote, expectedAddress, expectedPort) {
  return Buffer.isBuffer(message) && message.length > 0 && message.length <= MAX_DATAGRAM_SIZE &&
    remote?.family === "IPv4" && remote.address === expectedAddress && remote.port === expectedPort;
}

export function main(argv = process.argv.slice(2)) {
  const options = readArguments(argv);
  const lan = dgram.createSocket("udp4");
  const loopback = dgram.createSocket("udp4");
  let loopbackPort = 0;
  let closed = false;

  const close = () => {
    if (closed) return;
    closed = true;
    clearInterval(probeTimer);
    lan.close();
    loopback.close();
  };

  lan.on("message", (message, remote) => {
    if (!isAllowedDatagram(message, remote, options.phone, options.phonePort) || loopbackPort === 0) return;
    loopback.send(message, options.unityPort, "127.0.0.1");
  });
  loopback.on("message", (message, remote) => {
    if (!isAllowedDatagram(message, remote, "127.0.0.1", options.unityPort)) return;
    lan.send(message, options.phonePort, options.phone);
  });
  lan.on("error", close);
  loopback.on("error", close);
  loopback.bind(0, "127.0.0.1", () => { loopbackPort = loopback.address().port; });
  lan.bind(options.listenPort, "0.0.0.0");

  // An outbound probe establishes only the OS response path to this exact phone
  // endpoint. The Android decoder discards the deliberately invalid one-byte body.
  const probeTimer = setInterval(() => {
    if (!closed) lan.send(Buffer.from([0]), options.phonePort, options.phone);
  }, 750);
  probeTimer.unref();
  process.once("SIGINT", close);
  process.once("SIGTERM", close);
}

if (import.meta.url === `file:///${process.argv[1]?.replaceAll("\\", "/")}`) main();
