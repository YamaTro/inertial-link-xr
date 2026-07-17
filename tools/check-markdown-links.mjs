#!/usr/bin/env node
import {
  existsSync,
  lstatSync,
  realpathSync,
  readdirSync,
  readFileSync,
} from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { redactError } from "./lib/cli.mjs";

const SKIP_DIRECTORIES = new Set([
  ".git", ".gradle", ".gradle-user", ".tools", "Library", "Logs", "Obj",
  "Temp", "UserSettings", "bin", "build", "node_modules", "obj",
]);
const INLINE_LINK = /!?\[[^\]\n]*\]\(\s*(<[^>\n]+>|[^\s)]+)(?:\s+(?:"[^"]*"|'[^']*'|\([^)]*\)))?\s*\)/g;
const REFERENCE_LINK = /^\s{0,3}\[[^\]\n]+\]:\s*(<[^>\n]+>|\S+)/gm;

function markdownFiles(root) {
  const results = [];
  const visit = (directory) => {
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
      if (entry.isSymbolicLink()) continue;
      const full = path.join(directory, entry.name);
      if (entry.isDirectory()) {
        if (!SKIP_DIRECTORIES.has(entry.name)) visit(full);
      } else if (entry.isFile() && entry.name.toLowerCase().endsWith(".md")) {
        results.push(full);
      }
    }
  };
  visit(root);
  return results.sort();
}

function withoutFencedCode(markdown) {
  const output = [];
  let fence = null;
  for (const line of markdown.split(/\r?\n/)) {
    const marker = /^\s{0,3}(```+|~~~+)/.exec(line)?.[1] ?? null;
    if (marker !== null) {
      if (fence === null) fence = marker[0];
      else if (marker[0] === fence) fence = null;
      output.push("");
      continue;
    }
    output.push(fence === null ? line : "");
  }
  return output.join("\n");
}

function targets(markdown) {
  const result = [];
  for (const expression of [INLINE_LINK, REFERENCE_LINK]) {
    expression.lastIndex = 0;
    let match;
    while ((match = expression.exec(markdown)) !== null) result.push(match[1]);
  }
  return result;
}

function resolveLocalTarget(root, sourceFile, rawTarget) {
  let target = rawTarget;
  if (target.startsWith("<") && target.endsWith(">")) target = target.slice(1, -1);
  if (target === "" || target.startsWith("#") || target.startsWith("//")) return null;
  if (/^(https?:|mailto:)/i.test(target)) return null;
  if (/^[A-Za-z][A-Za-z0-9+.-]*:/.test(target)) {
    throw new Error(`${path.relative(root, sourceFile)}: unsupported link scheme in ${rawTarget}`);
  }

  const pathPart = target.split(/[?#]/, 1)[0];
  if (pathPart === "") return null;
  let decoded;
  try {
    decoded = decodeURIComponent(pathPart);
  } catch {
    throw new Error(`${path.relative(root, sourceFile)}: malformed percent-encoding in ${rawTarget}`);
  }
  if (decoded.includes("\0")) throw new Error(`${path.relative(root, sourceFile)}: NUL in link target`);

  const resolved = decoded.startsWith("/")
    ? path.resolve(root, decoded.slice(1))
    : path.resolve(path.dirname(sourceFile), decoded);
  const relative = path.relative(root, resolved);
  if (relative === ".." || relative.startsWith(`..${path.sep}`) || path.isAbsolute(relative)) {
    throw new Error(`${path.relative(root, sourceFile)}: local link escapes repository: ${rawTarget}`);
  }
  return resolved;
}

export function checkMarkdownLinks(rootValue) {
  const root = path.resolve(rootValue);
  if (!lstatSync(root).isDirectory()) throw new Error("repository root must be a directory");
  const realRoot = realpathSync(root);
  let checked = 0;
  const failures = [];
  const files = markdownFiles(root);
  for (const file of files) {
    const markdown = withoutFencedCode(readFileSync(file, "utf8"));
    for (const rawTarget of targets(markdown)) {
      checked += 1;
      try {
        const resolved = resolveLocalTarget(root, file, rawTarget);
        if (resolved !== null) {
          if (!existsSync(resolved)) {
            failures.push(`${path.relative(root, file)}: missing local target ${rawTarget}`);
            continue;
          }
          const realTarget = realpathSync(resolved);
          const realRelative = path.relative(realRoot, realTarget);
          if (realRelative === ".." || realRelative.startsWith(`..${path.sep}`) || path.isAbsolute(realRelative)) {
            failures.push(`${path.relative(root, file)}: local link resolves outside repository: ${rawTarget}`);
          }
        }
      } catch (error) {
        failures.push(error instanceof Error ? error.message : "unknown link error");
      }
    }
  }
  if (failures.length !== 0) throw new Error(failures.join("\n"));
  return { files: files.length, links: checked };
}

function main() {
  const defaultRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
  const root = process.argv[2] === undefined ? defaultRoot : path.resolve(process.argv[2]);
  if (process.argv.length > 3) throw new Error("Usage: node tools/check-markdown-links.mjs [REPOSITORY_ROOT]");
  const result = checkMarkdownLinks(root);
  process.stdout.write(`checked ${result.links} Markdown link(s) in ${result.files} file(s)\n`);
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    main();
  } catch (error) {
    process.stderr.write(`check-markdown-links: ${redactError(error)}\n`);
    process.exitCode = 1;
  }
}
