#!/usr/bin/env node
// Distribution pipeline for the remote-only Tauri desktop shell.
//
// The shell bundles NO backend, NO Next.js renderer, and NO Node runtime — it
// loads the live web app over HTTPS — so the production build is just
// `tauri build` over src-tauri/ (Rust core + the tiny bundled splash).
//
// Usage:
//   node scripts/tauri-dist.cjs build     # tauri build → NSIS (Windows) + dmg (macOS)
//
// Signing:
//   - Updater artifacts: set TAURI_SIGNING_PRIVATE_KEY / TAURI_SIGNING_PRIVATE_KEY_PASSWORD
//     (minisign; the matching pubkey lives in src-tauri/tauri.conf.json).
//   - Installer code-signing (Windows Authenticode / Apple notarization) is
//     currently DISABLED — builds are unsigned. See README "Desktop signing"
//     for how to re-enable.

const { spawnSync } = require('child_process');
const path = require('path');

const repoRoot = path.resolve(__dirname, '..');
const TAURI_CLI = '@tauri-apps/cli@2.11.3'; // pinned exact (verified June 2026)

function run(cmd, args) {
  console.log(`[tauri-dist] ${cmd} ${args.join(' ')}`);
  const result = spawnSync(cmd, args, {
    stdio: 'inherit',
    cwd: repoRoot,
    shell: process.platform === 'win32',
  });
  if (result.status !== 0) {
    throw new Error(`${cmd} exited with ${result.status}`);
  }
}

function buildAll() {
  run('pnpm', ['dlx', TAURI_CLI, 'build']);
}

const command = process.argv[2] || 'build';
try {
  if (command === 'build') buildAll();
  else throw new Error(`Unknown command: ${command} (only 'build' is supported in the remote-only shell)`);
} catch (error) {
  console.error(`[tauri-dist] FAILED: ${error.message}`);
  process.exit(1);
}
