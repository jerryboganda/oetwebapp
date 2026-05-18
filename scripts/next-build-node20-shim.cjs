const { spawnSync } = require('node:child_process');
const { existsSync } = require('node:fs');
const { join } = require('node:path');

const nextBin = join(process.cwd(), 'node_modules', 'next', 'dist', 'bin', 'next');

if (!existsSync(nextBin)) {
  console.error('[next-build] Missing local Next.js binary. Run npm install first.');
  process.exit(1);
}

const result = spawnSync(process.execPath, [nextBin, 'build'], {
  stdio: 'inherit',
  env: process.env,
});

if (result.error) {
  console.error(`[next-build] Failed to start Next.js build: ${result.error.message}`);
  process.exit(1);
}

process.exit(result.status ?? 1);
