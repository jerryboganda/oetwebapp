const { spawn } = require('child_process');
const path = require('path');

const frontendBaseUrl = (process.env.ELECTRON_RENDERER_URL || process.env.PLAYWRIGHT_BASE_URL || 'http://localhost:3000').replace(/\/$/, '');
const apiBaseUrl = (process.env.ELECTRON_API_URL || process.env.PLAYWRIGHT_API_URL || 'http://localhost:5198').replace(/\/$/, '');
const nodeCommand = process.execPath;
const qaReadinessScript = path.join(__dirname, 'qa', 'assert-local-stack.mjs');

let readinessProcess = null;
let electronProcess = null;
let shuttingDown = false;

function shutdown(code = 0) {
  if (shuttingDown) {
    return;
  }

  shuttingDown = true;

  if (readinessProcess && !readinessProcess.killed) {
    readinessProcess.kill();
  }

  if (electronProcess && !electronProcess.killed) {
    electronProcess.kill();
  }

  process.exit(code);
}

function runReadinessCheck() {
  return new Promise((resolve, reject) => {
    readinessProcess = spawn(nodeCommand, [qaReadinessScript], {
      stdio: 'inherit',
      env: {
        ...process.env,
        PLAYWRIGHT_BASE_URL: frontendBaseUrl,
        PLAYWRIGHT_API_URL: apiBaseUrl,
      },
      shell: false,
    });

    readinessProcess.on('error', reject);
    readinessProcess.on('exit', (code, signal) => {
      readinessProcess = null;

      if (code === 0) {
        resolve();
        return;
      }

      reject(new Error(`[electron-dev] Docker desktop baseline readiness failed (code ${code ?? 'unknown'}${signal ? `, signal ${signal}` : ''}).`));
    });
  });
}

async function main() {
  await runReadinessCheck();

  electronProcess = spawn(process.platform === 'win32' ? 'cmd.exe' : 'npm', process.platform === 'win32' ? ['/c', 'npm', 'exec', '--', 'electron', '.'] : ['exec', '--', 'electron', '.'], {
    stdio: 'inherit',
    env: {
      ...process.env,
      ELECTRON_RENDERER_URL: frontendBaseUrl,
      NEXT_PUBLIC_API_BASE_URL: process.env.NEXT_PUBLIC_API_BASE_URL || '/api/backend',
      API_PROXY_TARGET_URL: process.env.API_PROXY_TARGET_URL || apiBaseUrl,
    },
    shell: false,
  });

  electronProcess.on('exit', (code) => {
    shutdown(typeof code === 'number' ? code : 0);
  });
}

process.on('SIGINT', () => shutdown(0));
process.on('SIGTERM', () => shutdown(0));

main().catch((error) => {
  console.error('[electron-dev] failed to start desktop shell', error);
  shutdown(1);
});
