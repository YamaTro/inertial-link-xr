#!/usr/bin/env node
import dgram from "node:dgram";
import {
  PROTOCOL,
  ReplayWindow,
  decodePacket,
  formatSessionId,
} from "./lib/ilxr.mjs";
import {
  assertAddressAllowed,
  isLoopbackAddress,
  normalizeIpAddress,
  parseArguments,
  parseBoundedInteger,
  redactError,
} from "./lib/cli.mjs";
import { consumePairingKey } from "./lib/key-input.mjs";

const HELP = `Usage:
  node tools/packet-inspector.mjs [--key PUBLIC_TEST_HEX] [options]

Key input:
  ILXR_PAIRING_KEY      preferred one-session key; required off loopback
  --key HEX             loopback-only public-test-vector convenience

Options:
  --bind IP             literal bind address (default 127.0.0.1)
  --port N              UDP port (default 28461)
  --count N             exit after N accepted packets (default: keep running)
  --allow-network       permit a non-loopback bind
  --help

The inspector authenticates and validates packets before printing summaries.
It never prints a key or raw datagram.
`;

async function main() {
  const args = parseArguments(process.argv.slice(2), ["key", "bind", "port", "count", "allow-network", "help"]);
  if (args.help) {
    process.stdout.write(HELP);
    return;
  }
  const bind = args.bind ?? "127.0.0.1";
  assertAddressAllowed(bind, args["allow-network"] === true);
  const key = consumePairingKey({
    argumentValue: args.key,
    requireEnvironment: !isLoopbackAddress(bind),
  });
  try {
    await runInspector(args, key, bind);
  } finally {
    key.fill(0);
  }
}

async function runInspector(args, key, bind) {
  const port = parseBoundedInteger("port", args.port, PROTOCOL.defaultPort, 1024, 65_535);
  const count = args.count === undefined
    ? Number.MAX_SAFE_INTEGER
    : parseBoundedInteger("count", args.count, 1, 1, 1_000_000);
  const replay = new ReplayWindow();
  const socket = dgram.createSocket(bind.includes(":") ? "udp6" : "udp4");
  let accepted = 0;
  let rejected = 0;

  await new Promise((resolve, reject) => {
    let settled = false;
    const finish = (error) => {
      if (settled) return;
      settled = true;
      process.off("SIGINT", shutdown);
      process.off("SIGTERM", shutdown);
      if (error) reject(error);
      else resolve();
    };
    const shutdown = () => {
      try { socket.close(); } catch { finish(); }
    };

    socket.on("message", (message, remote) => {
      try {
        const packet = decodePacket(message, key);
        const endpoint = `${normalizeIpAddress(remote.address)}:${remote.port}`;
        if (!replay.accept(packet.header.sessionId, packet.header.sequence, endpoint)) {
          throw new Error("replay/too-old sequence");
        }
        accepted += 1;
        const summary = {
          accepted,
          type: packet.header.type,
          sessionId: formatSessionId(packet.header.sessionId),
          sequence: packet.header.sequence,
          remote: `${remote.address}:${remote.port}`,
        };
        if (packet.header.type === PROTOCOL.type.imu) {
          Object.assign(summary, {
            linearAccel: packet.payload.linearAccel.map((value) => Number(value.toFixed(4))),
            gyro: packet.payload.gyro.map((value) => Number(value.toFixed(4))),
            calibrationId: packet.payload.calibrationId,
            statusBits: `0x${packet.payload.statusBits.toString(16).padStart(8, "0")}`,
          });
        }
        process.stdout.write(`${JSON.stringify(summary)}\n`);
        if (accepted >= count) socket.close();
      } catch {
        rejected += 1;
        if (rejected === 1 || rejected % 100 === 0) {
          process.stderr.write(`Rejected ${rejected} invalid or replayed packet(s).\n`);
        }
      }
    });

    socket.on("listening", () => {
      process.stdout.write(`Authenticated ILXR inspector listening on ${bind}:${port}\n`);
    });
    socket.once("close", () => {
      process.stdout.write(`Accepted ${accepted}; rejected ${rejected}.\n`);
      finish();
    });
    socket.once("error", (error) => {
      try { socket.close(); } catch { /* already closed */ }
      finish(error);
    });
    process.once("SIGINT", shutdown);
    process.once("SIGTERM", shutdown);
    socket.bind(port, bind);
  });
}

main().catch((error) => {
  process.stderr.write(`packet-inspector: ${redactError(error)}\n`);
  process.exitCode = 1;
});
