#!/usr/bin/env node
import {
  readFileSync,
  readdirSync,
  statSync,
} from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { validateImuPayload } from "./lib/ilxr.mjs";
import { redactError } from "./lib/cli.mjs";

const MAXIMUM_FILE_BYTES = 5 * 1024 * 1024;
const METADATA_KEYS = ["coordinates", "generator", "kind", "synthetic", "units", "version"];
const SAMPLE_KEYS = [
  "calibrationId",
  "gravity",
  "gyro",
  "kind",
  "linearAccel",
  "rawAccel",
  "rotation",
  "statusBits",
  "tNs",
];

function exactKeys(value, expected, context) {
  if (value === null || typeof value !== "object" || Array.isArray(value)) throw new Error(`${context} must be an object`);
  const actual = Object.keys(value).sort();
  if (actual.length !== expected.length || actual.some((key, index) => key !== expected[index])) {
    throw new Error(`${context} has unknown or missing fields: ${actual.join(", ")}`);
  }
}

function expandInputs(inputs) {
  const expanded = [];
  for (const input of inputs) {
    if (!input.includes("*")) {
      expanded.push(input);
      continue;
    }
    const directory = path.dirname(input);
    const escaped = path.basename(input).replace(/[.+?^${}()|[\]\\]/g, "\\$&").replaceAll("*", ".*");
    const pattern = new RegExp(`^${escaped}$`);
    for (const name of readdirSync(directory).sort()) if (pattern.test(name)) expanded.push(path.join(directory, name));
  }
  return expanded;
}

export function validateRecording(file) {
  if (statSync(file).size > MAXIMUM_FILE_BYTES) throw new Error(`${file}: exceeds 5 MiB validation limit`);
  const lines = readFileSync(file, "utf8").split(/\r?\n/).filter((line) => line.trim() !== "");
  if (lines.length < 2) throw new Error(`${file}: metadata and at least one sample are required`);
  let metadata;
  try {
    metadata = JSON.parse(lines[0]);
  } catch {
    throw new Error(`${file}:1: invalid JSON`);
  }
  exactKeys(metadata, METADATA_KEYS, `${file}:1 metadata`);
  if (metadata.kind !== "ilxr-recording" || metadata.version !== 1 || metadata.synthetic !== true || metadata.units !== "SI") {
    throw new Error(`${file}:1: unsupported or non-synthetic metadata`);
  }
  if (metadata.coordinates !== "OpenXR RH +X right +Y up -Z forward") throw new Error(`${file}:1: unexpected coordinate system`);
  if (typeof metadata.generator !== "string" || metadata.generator.length < 3 || metadata.generator.length > 120) {
    throw new Error(`${file}:1: generator must be a short deterministic description`);
  }
  let prior = -1n;
  for (let index = 1; index < lines.length; index += 1) {
    let sample;
    try {
      sample = JSON.parse(lines[index]);
    } catch {
      throw new Error(`${file}:${index + 1}: invalid JSON`);
    }
    exactKeys(sample, SAMPLE_KEYS, `${file}:${index + 1} sample`);
    if (sample.kind !== "sample" || !/^(0|[1-9][0-9]*)$/.test(sample.tNs)) {
      throw new Error(`${file}:${index + 1}: invalid sample kind or relative tNs`);
    }
    const time = BigInt(sample.tNs);
    if (time <= prior || time > 0x7fff_ffff_ffff_ffffn) throw new Error(`${file}:${index + 1}: tNs must strictly increase within int64`);
    prior = time;
    validateImuPayload({
      senderSendTimeNs: 1n,
      rawAccel: sample.rawAccel,
      gyro: sample.gyro,
      gravity: sample.gravity,
      linearAccel: sample.linearAccel,
      rotation: sample.rotation,
      calibrationId: sample.calibrationId,
      statusBits: sample.statusBits,
    });
    const calibrated = (sample.statusBits & (1 << 5)) !== 0;
    const calibrating = (sample.statusBits & (1 << 6)) !== 0;
    if (sample.calibrationId !== 0 && !calibrated) {
      throw new Error(`${file}:${index + 1}: non-zero calibrationId requires CALIBRATED in repository fixtures`);
    }
    if (calibrated && calibrating) {
      throw new Error(`${file}:${index + 1}: CALIBRATED and CALIBRATING cannot both be set in repository fixtures`);
    }
  }
  return lines.length - 1;
}

function main() {
  const inputs = process.argv.slice(2);
  if (inputs.includes("--help")) {
    process.stdout.write("Usage: node tools/validate-recording.mjs FILE.ndjson [...]\n");
    return;
  }
  const defaults = [path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../recordings/*.ndjson")];
  const files = expandInputs(inputs.length === 0 ? defaults : inputs);
  if (files.length === 0) throw new Error("no recording files matched");
  let total = 0;
  for (const file of files) {
    const samples = validateRecording(file);
    total += samples;
    process.stdout.write(`valid ${file} (${samples} synthetic samples)\n`);
  }
  process.stdout.write(`validated ${files.length} file(s), ${total} sample(s)\n`);
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    main();
  } catch (error) {
    process.stderr.write(`validate-recording: ${redactError(error)}\n`);
    process.exitCode = 1;
  }
}
