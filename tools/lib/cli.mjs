import { isIP } from "node:net";

export function parseArguments(argv, allowedNames = null) {
  const result = Object.create(null);
  const allowed = allowedNames === null ? null : new Set(allowedNames);
  for (let index = 0; index < argv.length; index += 1) {
    const token = argv[index];
    if (!token.startsWith("--")) throw new Error(`unexpected argument: ${token}`);
    const name = token.slice(2);
    if (allowed !== null && !allowed.has(name)) throw new Error(`unknown option: --${name}`);
    if (Object.hasOwn(result, name)) throw new Error(`duplicate option: --${name}`);
    if (name === "allow-network" || name === "help") {
      result[name] = true;
      continue;
    }
    const value = argv[index + 1];
    if (value === undefined || value.startsWith("--")) throw new Error(`missing value for --${name}`);
    result[name] = value;
    index += 1;
  }
  return result;
}

export function parseBoundedInteger(name, value, fallback, minimum, maximum) {
  const parsed = value === undefined ? fallback : Number(value);
  if (!Number.isInteger(parsed) || parsed < minimum || parsed > maximum) {
    throw new Error(`--${name} must be an integer from ${minimum} to ${maximum}`);
  }
  return parsed;
}

export function assertAddressAllowed(address, allowNetwork) {
  if (isIP(address) === 0) throw new Error("host/bind must be a literal IP address; DNS names are intentionally unsupported");
  const normalized = normalizeIpAddress(address);
  const version = isIP(normalized);
  if (version === 4) {
    const octets = normalized.split(".").map(Number);
    if (octets[0] === 0 || octets[0] >= 224 || octets[3] === 255) {
      throw new Error("unspecified, multicast, reserved, and broadcast IPv4 addresses are not allowed");
    }
  } else if (normalized === "::" || normalized.startsWith("ff")) {
    throw new Error("unspecified and multicast IPv6 addresses are not allowed");
  }
  const loopback = isLoopbackAddress(address);
  if (!loopback && !allowNetwork) {
    throw new Error("non-loopback networking is disabled; pass --allow-network after reviewing SECURITY.md");
  }
}

export function isLoopbackAddress(address) {
  const normalized = normalizeIpAddress(address);
  return normalized.startsWith("127.") || normalized === "::1";
}

export function normalizeIpAddress(address) {
  const mapped = /^::ffff:(\d+\.\d+\.\d+\.\d+)$/i.exec(address);
  if (mapped) return normalizeIpAddress(mapped[1]);
  const version = isIP(address);
  if (version === 4) return address.split(".").map((part) => String(Number(part))).join(".");
  if (version === 6) {
    const hostname = new URL(`http://[${address}]/`).hostname;
    return hostname.slice(1, -1).toLowerCase();
  }
  throw new Error("address must be a literal IPv4 or IPv6 address");
}

export function isExpectedRemote(remote, expectedAddress, expectedPort) {
  return Number(remote?.port) === expectedPort &&
    normalizeIpAddress(String(remote?.address ?? "")) === normalizeIpAddress(expectedAddress);
}

export function redactError(error) {
  return error instanceof Error ? error.message : "unknown error";
}
