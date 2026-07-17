import { PROTOCOL, ReplayWindow } from "./ilxr.mjs";
import { isExpectedRemote, normalizeIpAddress } from "./cli.mjs";

export class SyncRequestGuard {
  #expectedHost;
  #expectedPort;
  #sessionId;
  #replay = new ReplayWindow({ maximumSessions: 1 });
  #recentNonces = new Set();

  constructor({ expectedHost, expectedPort, sessionId }) {
    this.#expectedHost = expectedHost;
    this.#expectedPort = expectedPort;
    this.#sessionId = sessionId;
  }

  accept(packet, remote) {
    if (!isExpectedRemote(remote, this.#expectedHost, this.#expectedPort)) return false;
    if (packet?.header?.sessionId !== this.#sessionId) return false;
    if (packet.header.type !== PROTOCOL.type.syncRequest) return false;
    if (!Number.isInteger(packet.header.sequence) || packet.header.sequence < 0 || packet.header.sequence > 0xffff_ffff) return false;
    if (typeof packet.header.eventTimeNs !== "bigint" || packet.header.eventTimeNs <= 0n) return false;
    if (typeof packet?.payload?.t0 !== "bigint" || packet.header.eventTimeNs !== packet.payload.t0) return false;
    if (typeof packet.payload.nonce !== "bigint" || packet.payload.nonce < 0n || packet.payload.nonce > 0xffff_ffff_ffff_ffffn) return false;

    const endpoint = `${normalizeIpAddress(remote.address)}:${remote.port}`;
    if (!this.#replay.accept(packet.header.sessionId, packet.header.sequence, endpoint)) return false;

    const nonce = packet.payload.nonce.toString(16);
    if (this.#recentNonces.has(nonce)) return false;
    this.#recentNonces.add(nonce);
    while (this.#recentNonces.size > 64) this.#recentNonces.delete(this.#recentNonces.values().next().value);
    return true;
  }
}
