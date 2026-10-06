#!/usr/bin/env node
// MANUAL TOOL, INERT: run manually by the owner from a self-provisioned load generator; no CI runs
// this; agents never run it (AGENTS.md "NO AUTOMATED QA ANYWHERE"). Runbook: docs/ops/LOAD-TESTING.md.
//
// Writes `claude` and `codex` shell shims into a directory so the real writing sidecars (which spawn
// the CLIs by name) pick up the stub from PATH:
//   node install-shims.mjs /tmp/sim-bin && PATH=/tmp/sim-bin:$PATH node /app/claude/server.mjs

import { chmodSync, mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

export const ENGINES = Object.freeze(['claude', 'codex']);

export function shimScript(engine, stubPath) {
  return `#!/bin/sh\nexec node "${stubPath}" ${engine} "$@"\n`;
}

export function installShims(targetDir, stubPath = join(dirname(fileURLToPath(import.meta.url)), 'cli-stub.mjs')) {
  const dir = resolve(targetDir);
  mkdirSync(dir, { recursive: true });
  const written = [];
  for (const engine of ENGINES) {
    const file = join(dir, engine);
    writeFileSync(file, shimScript(engine, stubPath));
    chmodSync(file, 0o755);
    written.push(file);
  }
  return written;
}

const isMain = process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href;
if (isMain) {
  const target = process.argv[2];
  if (!target) {
    process.stderr.write('usage: install-shims.mjs <directory>\n');
    process.exitCode = 2;
  } else {
    for (const file of installShims(target)) process.stdout.write(`${file}\n`);
  }
}
