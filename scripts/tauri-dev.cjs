#!/usr/bin/env node
// Dev runner for the remote-only Tauri desktop shell.
//
// No bundled backend or renderer to prepare — the shell loads a remote web app.
// By default it loads the production URL; to develop against a local Next.js
// server, run `pnpm dev` (http://localhost:3000) in another terminal and set
// OET_DESKTOP_WEB_URL=http://localhost:3000 before launching this script. The
// shell's navigation guard only permits loopback origins in dev builds.

const { spawnSync } = require('child_process');
const path = require('path');
const os = require('os');

const repoRoot = path.resolve(__dirname, '..');
const cargo = process.env.CARGO || path.join(os.homedir(), '.cargo', 'bin', 'cargo');

const result = spawnSync(
  cargo,
  ['run', '--manifest-path', path.join(repoRoot, 'src-tauri', 'Cargo.toml')],
  { stdio: 'inherit', env: { ...process.env } },
);
process.exit(result.status ?? 1);
