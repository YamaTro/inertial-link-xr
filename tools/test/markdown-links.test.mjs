import assert from "node:assert/strict";
import { mkdtempSync, mkdirSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import test from "node:test";
import { checkMarkdownLinks } from "../check-markdown-links.mjs";

test("Markdown checker decodes local paths and rejects repository escapes", () => {
  const root = mkdtempSync(path.join(tmpdir(), "ilxr-links-"));
  try {
    mkdirSync(path.join(root, "docs"));
    writeFileSync(path.join(root, "docs", "target file.md"), "# Target\n", "utf8");
    writeFileSync(path.join(root, "README.md"), "[target](docs/target%20file.md#target)\n", "utf8");
    assert.deepEqual(checkMarkdownLinks(root), { files: 2, links: 1 });

    writeFileSync(path.join(root, "README.md"), "[escape](%2e%2e/outside.md)\n", "utf8");
    assert.throws(() => checkMarkdownLinks(root), /escapes repository/);

    writeFileSync(path.join(root, "README.md"), "[unsafe](file:///outside.md)\n", "utf8");
    assert.throws(() => checkMarkdownLinks(root), /unsupported link scheme/);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});
