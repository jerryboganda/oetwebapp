#!/usr/bin/env node
// Distribution pipeline for the Tauri desktop shell (parallel to
// scripts/desktop-dist.cjs, which stays frozen for Electron until Phase 6).
//
// Usage:
//   node scripts/tauri-dist.cjs publish-backend    # dotnet publish -> desktop-backend-runtime
//   node scripts/tauri-dist.cjs sync-standalone    # copy .next/static + public into .next/standalone
//   node scripts/tauri-dist.cjs stage-node         # download/copy node binary into src-tauri/binaries
//   node scripts/tauri-dist.cjs build              # all of the above + next build + tauri build
//
// Signing (Phase 4): set TAURI_SIGNING_PRIVATE_KEY/-_PASSWORD for updater
// artifacts and AZURE code-signing env (same as electron pipeline) — the NSIS
// bundle honors bundle>windows>signCommand when configured in tauri.dist.conf.json.

const { spawnSync } = require('child_process');
const fs = require('fs');
const path = require('path');

const repoRoot = path.resolve(__dirname, '..');
const srcTauri = path.join(repoRoot, 'src-tauri');

function run(cmd, args, opts = {}) {
  console.log(`[tauri-dist] ${cmd} ${args.join(' ')}`);
  const result = spawnSync(cmd, args, { stdio: 'inherit', cwd: repoRoot, shell: process.platform === 'win32', ...opts });
  if (result.status !== 0) {
    throw new Error(`${cmd} exited with ${result.status}`);
  }
}

function publishBackend() {
  const rid = process.platform === 'win32' ? 'win-x64' : process.arch === 'arm64' ? 'osx-arm64' : 'osx-x64';
  run('dotnet', [
    'publish', 'backend/src/OetLearner.Api/OetLearner.Api.csproj',
    '-c', 'Release', '-r', rid, '--self-contained', 'true',
    '-o', path.join(repoRoot, 'desktop-backend-runtime'),
  ]);
}

function syncStandalone() {
  // Port of electron-builder.config.cjs syncStandaloneRuntime: standalone
  // output needs .next/static and public copied next to server.js.
  const standalone = path.join(repoRoot, '.next', 'standalone');
  if (!fs.existsSync(path.join(standalone, 'server.js'))) {
    throw new Error('Run `pnpm run build` first — .next/standalone missing.');
  }
  fs.cpSync(path.join(repoRoot, '.next', 'static'), path.join(standalone, '.next', 'static'), { recursive: true });
  fs.cpSync(path.join(repoRoot, 'public'), path.join(standalone, 'public'), { recursive: true });
  console.log('[tauri-dist] standalone runtime synced');

  // BUG-011b: pnpm leaves symlinks/junctions in standalone/node_modules
  // (next, react, react-dom, @next -> ../node_modules/.pnpm/...). Tauri's
  // resource copy does not preserve them, so the bundled renderer fails with
  // "Cannot find module 'next'". Produce a DEREFERENCED copy (real files) that
  // the bundle points at. The source tree is left untouched (no deletes near
  // the pnpm store), so this is safe to re-run.
  const distStandalone = path.join(repoRoot, '.next', 'standalone-dist');
  fs.rmSync(distStandalone, { recursive: true, force: true });
  fs.cpSync(standalone, distStandalone, { recursive: true, dereference: true });
  console.log('[tauri-dist] dereferenced standalone -> .next/standalone-dist');
}

function stageNode() {
  // Ship the same Node major the renderer was tested with. For now: copy the
  // build machine's node.exe; CI should download the official binary per
  // target triple instead.
  const binaries = path.join(srcTauri, 'binaries');
  fs.mkdirSync(binaries, { recursive: true });
  const probe = spawnSync(process.platform === 'win32' ? 'where' : 'which', ['node'], { encoding: 'utf8', shell: true });
  const nodePath = probe.stdout.split(/\r?\n/).find(Boolean);
  if (!nodePath) throw new Error('node not found on PATH');
  const target = path.join(binaries, process.platform === 'win32' ? 'node.exe' : 'node');
  fs.copyFileSync(nodePath.trim(), target);
  console.log(`[tauri-dist] staged ${nodePath.trim()} -> ${target}`);
}

function buildAll() {
  publishBackend();
  run('pnpm', ['run', 'build'], {
    env: {
      ...process.env,
      NODE_ENV: 'production',
      NEXT_TELEMETRY_DISABLED: '1',
      NEXT_PUBLIC_API_BASE_URL: process.env.NEXT_PUBLIC_API_BASE_URL || '/api/backend',
      APP_URL: process.env.APP_URL || 'http://localhost:3000',
    },
  });
  syncStandalone();
  stageNode();
  // The dist config can't hardcode the node binary name — Windows ships
  // `node.exe`, macOS/Linux ship `node`. Merge the platform-correct node
  // resource into the dist config and build from the merged result. (Hardcoding
  // `binaries/node.exe` previously failed the macOS bundle: "resource path
  // `binaries/node.exe` doesn't exist".)
  const distConf = JSON.parse(fs.readFileSync(path.join(srcTauri, 'tauri.dist.conf.json'), 'utf8'));
  distConf.bundle = distConf.bundle || {};
  distConf.bundle.resources = distConf.bundle.resources || {};
  if (process.platform === 'win32') {
    distConf.bundle.resources['binaries/node.exe'] = 'node.exe';
  } else {
    distConf.bundle.resources['binaries/node'] = 'node';
  }
  const buildConf = path.join(srcTauri, 'tauri.build.conf.json');
  fs.writeFileSync(buildConf, JSON.stringify(distConf, null, 2));
  run('pnpm', ['dlx', '@tauri-apps/cli@^2', 'build', '--config', buildConf]);
}

const command = process.argv[2] || 'build';
try {
  if (command === 'publish-backend') publishBackend();
  else if (command === 'sync-standalone') syncStandalone();
  else if (command === 'stage-node') stageNode();
  else if (command === 'build') buildAll();
  else throw new Error(`Unknown command: ${command}`);
} catch (error) {
  console.error(`[tauri-dist] FAILED: ${error.message}`);
  process.exit(1);
}
