#!/usr/bin/env node
import dgram from "node:dgram";
import {
  IMU_STATUS,
  PROTOCOL,
  decodePacket,
  encodeImuPacket,
  encodeSyncResponse,
  formatSessionId,
  generateSessionId,
} from "./lib/ilxr.mjs";
import {
  assertAddressAllowed,
  isLoopbackAddress,
  parseArguments,
  parseBoundedInteger,
  redactError,
} from "./lib/cli.mjs";
import { consumePairingKey } from "./lib/key-input.mjs";
import { SyncRequestGuard } from "./lib/sync-request-guard.mjs";

const HELP = `Usage:
  node tools/synthetic-sender.mjs [--key PUBLIC_TEST_HEX] [options]

Key input:
  ILXR_PAIRING_KEY      preferred one-session key; required off loopback
  --key HEX             loopback-only public-test-vector convenience

Options:
  --host IP             destination literal IP (default 127.0.0.1)
  --port N              destination UDP port (default 28461)
  --hz N                sample rate 1..120 (default 60)
  --seconds N           duration 1..300 (default 10)
  --scenario NAME       stationary | gentle-turn | gentle-brake
  --allow-network       permit a non-loopback destination
  --help

This tool emits synthetic data only. It is not a motion-sickness treatment.
`;

function sampleFor(scenario, seconds) {
  const gravity = [0, 9.80665, 0];
  let linearAccel = [0, 0, 0];
  let gyro = [0, 0, 0];
  let rotation = [0, 0, 0, 1];
  if (scenario === "gentle-turn") {
    const frequency = 0.15;
    const phase = 2 * Math.PI * frequency * seconds;
    const lateral = 0.8 * Math.sin(phase);
    const yawRate = 0.12 * Math.sin(phase);
    const yaw = (0.12 / (2 * Math.PI * frequency)) * (1 - Math.cos(phase));
    linearAccel = [lateral, 0, 0];
    gyro = [0, yawRate, 0];
    rotation = [0, Math.sin(yaw / 2), 0, Math.cos(yaw / 2)];
  } else if (scenario === "gentle-brake") {
    const envelope = Math.sin(Math.min(1, seconds / 3) * Math.PI);
    linearAccel = [0, 0, Math.max(0, envelope) * 0.7];
  }
  return {
    rawAccel: linearAccel.map((value, index) => value + gravity[index]),
    gyro,
    gravity,
    linearAccel,
    rotation,
    calibrationId: 1,
    statusBits:
      IMU_STATUS.rawAccelValid |
      IMU_STATUS.gyroscopeValid |
      IMU_STATUS.gravityValid |
      IMU_STATUS.linearAccelValid |
      IMU_STATUS.rotationValid |
      IMU_STATUS.calibrated |
      IMU_STATUS.accuracyHigh,
  };
}

async function main() {
  const args = parseArguments(process.argv.slice(2), [
    "key", "host", "port", "hz", "seconds", "scenario", "allow-network", "help",
  ]);
  if (args.help) {
    process.stdout.write(HELP);
    return;
  }
  const host = args.host ?? "127.0.0.1";
  assertAddressAllowed(host, args["allow-network"] === true);
  const key = consumePairingKey({
    argumentValue: args.key,
    requireEnvironment: !isLoopbackAddress(host),
  });
  try {
    await runSender(args, key);
  } finally {
    key.fill(0);
  }
}

async function runSender(args, key) {
  const host = args.host ?? "127.0.0.1";
  const port = parseBoundedInteger("port", args.port, PROTOCOL.defaultPort, 1024, 65_535);
  const hz = parseBoundedInteger("hz", args.hz, 60, 1, 120);
  const seconds = parseBoundedInteger("seconds", args.seconds, 10, 1, 300);
  const scenario = args.scenario ?? "gentle-turn";
  if (!["stationary", "gentle-turn", "gentle-brake"].includes(scenario)) {
    throw new Error("--scenario must be stationary, gentle-turn, or gentle-brake");
  }

  const sessionId = generateSessionId();
  const socket = dgram.createSocket(host.includes(":") ? "udp6" : "udp4");
  try {
  let sequence = 0;
  let sent = 0;
  let syncResponses = 0;
  const syncGuard = new SyncRequestGuard({ expectedHost: host, expectedPort: port, sessionId });

  socket.on("message", (message, remote) => {
    const t1 = process.hrtime.bigint();
    try {
      const packet = decodePacket(message, key);
      if (!syncGuard.accept(packet, remote)) return;
      const t2 = process.hrtime.bigint();
      const response = encodeSyncResponse({
        sequence: sequence++,
        sessionId,
        eventTimeNs: t1,
        t0: packet.payload.t0,
        t1,
        t2,
        nonce: packet.payload.nonce,
      }, key);
      socket.send(response);
      syncResponses += 1;
    } catch {
      // Invalid UDP is intentionally ignored; never echo parser details.
    }
  });

  await new Promise((resolve, reject) => {
    const onError = (error) => reject(error);
    socket.once("error", onError);
    const localBind = isLoopbackAddress(host) ? host : (host.includes(":") ? "::" : "0.0.0.0");
    socket.bind(0, localBind, () => {
      socket.connect(port, host, () => {
        socket.off("error", onError);
        resolve();
      });
    });
  });

  process.stdout.write(
    `Synthetic ${scenario} session ${formatSessionId(sessionId)} -> ${host}:${port} at ${hz} Hz\n`,
  );
  process.stdout.write("Passenger-only Research Preview; no efficacy claim. Press Ctrl+C to stop.\n");

  const started = process.hrtime.bigint();
  const intervalMs = 1000 / hz;
  await new Promise((resolve, reject) => {
    let timer;
    const finish = (error) => {
      if (timer) clearInterval(timer);
      process.off("SIGINT", onSignal);
      process.off("SIGTERM", onSignal);
      socket.off("error", onError);
      if (error) reject(error);
      else resolve();
    };
    const onSignal = () => finish();
    const onError = (error) => finish(error);
    process.once("SIGINT", onSignal);
    process.once("SIGTERM", onSignal);
    socket.once("error", onError);
    timer = setInterval(() => {
      try {
        const eventTimeNs = process.hrtime.bigint();
        const elapsed = Number(eventTimeNs - started) / 1e9;
        if (elapsed >= seconds) {
          finish();
          return;
        }
        const imu = sampleFor(scenario, elapsed);
        imu.senderSendTimeNs = process.hrtime.bigint();
        const packet = encodeImuPacket({ sequence: sequence++, sessionId, eventTimeNs, imu }, key);
        socket.send(packet);
        sent += 1;
      } catch (error) {
        finish(error);
      }
    }, intervalMs);
  });

  await new Promise((resolve) => setTimeout(resolve, 100));
  process.stdout.write(`Sent ${sent} synthetic IMU packets and ${syncResponses} sync responses.\n`);
  } finally {
    try { socket.close(); } catch { /* already closed */ }
  }
}

main().catch((error) => {
  process.stderr.write(`synthetic-sender: ${redactError(error)}\n`);
  process.exitCode = 1;
});
