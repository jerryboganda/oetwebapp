#!/usr/bin/env node
// Dev runner for the Tauri desktop shell (parallel to scripts/electron-dev.cjs).
// Uses repo-local artifacts: .next/standalone (pnpm run build) and
// desktop-backend-runtime (dotnet publish) — see scripts/tauri-dist.cjs prepare().
// The shell itself spawns both sidecars; this script only checks prerequisites
// and launches `cargo run`.

const { spawnSync } = require('child_process');
const fs = require('fs');
const path = require('path');

const repoRoot = path.resolve(__dirname, '..');
const standalone = path.join(repoRoot, '.next', 'standalone', 'server.js');
const backendExe = path.join(
  repoRoot,
  'desktop-backend-runtime',
  process.platform === 'win32' ? 'OetLearner.Api.exe' : 'OetLearner.Api',
);

const missing = [];
if (!fs.existsSync(standalone)) missing.push(`renderer: ${standalone} (run: pnpm run build, then node scripts/tauri-dist.cjs sync-standalone)`);
if (!fs.existsSync(backendExe)) missing.push(`backend: ${backendExe} (run: node scripts/tauri-dist.cjs publish-backend)`);
if (missing.length > 0) {
  console.error('[tauri-dev] missing prerequisites:\n  - ' + missing.join('\n  - '));
  process.exit(1);
}

const cargo = process.env.CARGO || path.join(require('os').homedir(), '.cargo', 'bin', 'cargo');
const result = spawnSync(cargo, ['run', '--manifest-path', path.join(repoRoot, 'src-tauri', 'Cargo.toml')], {
  stdio: 'inherit',
  env: { ...process.env, OET_REPO_ROOT: repoRoot },
});
process.exit(result.status ?? 1);
