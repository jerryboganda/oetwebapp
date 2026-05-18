import { spawnSync } from 'node:child_process';
import { existsSync } from 'node:fs';
import { join } from 'node:path';

const nextBin = join(process.cwd(), 'node_modules', 'next', 'dist', 'bin', 'next');
const nodeMajor = Number.parseInt(process.versions.node.split('.')[0] ?? '0', 10);
const shouldUseNode20Shim = process.platform === 'win32' && nodeMajor >= 24;

if (!existsSync(nextBin)) {
  console.error('[next-build] Missing local Next.js binary. Run npm install first.');
  process.exit(1);
}

const command = shouldUseNode20Shim
  ? process.platform === 'win32'
    ? process.env.ComSpec ?? 'cmd.exe'
    : 'npx'
  : process.execPath;
const args = shouldUseNode20Shim
  ? process.platform === 'win32'
    ? [
        '/d',
        '/c',
        'npm exec --yes --package node@20 -- node scripts/next-build-node20-shim.cjs',
      ]
    : ['-y', 'node@20', nextBin, 'build']
  : [nextBin, 'build'];
if (shouldUseNode20Shim) {
  console.warn('[next-build] Windows Node 24 detected; using Node 20 for Next.js build workers.');
}

const result = spawnSync(command, args, {
  stdio: 'inherit',
  env: process.env,
});

if (result.error) {
  console.error(`[next-build] Failed to start Next.js build: ${result.error.message}`);
  process.exit(1);
}

process.exit(result.status ?? 1);
