const { spawnSync } = require('child_process');
const fs = require('fs');
const path = require('path');

function candidateExecutablePaths() {
  const desktopDir = path.join(__dirname, '..', 'dist', 'desktop');

  if (process.platform === 'win32') {
    return [
      path.join(desktopDir, 'win-unpacked', 'OET Prep.exe'),
    ];
  }

  if (process.platform === 'darwin') {
    return [
      path.join(desktopDir, 'mac-arm64', 'OET Prep.app', 'Contents', 'MacOS', 'OET Prep'),
      path.join(desktopDir, 'mac', 'OET Prep.app', 'Contents', 'MacOS', 'OET Prep'),
    ];
  }

  return [
    path.join(desktopDir, 'linux-unpacked', 'oet-prep'),
    path.join(desktopDir, 'linux-unpacked', 'OET Prep'),
  ];
}

function resolveExecutablePath() {
  if (process.env.ELECTRON_EXECUTABLE_PATH) {
    return process.env.ELECTRON_EXECUTABLE_PATH;
  }

  const executablePath = candidateExecutablePaths().find((candidatePath) => fs.existsSync(candidatePath));
  if (!executablePath) {
    throw new Error('Packaged desktop executable was not found. Run pnpm run desktop:dist first or set ELECTRON_EXECUTABLE_PATH.');
  }

  return executablePath;
}

function main() {
  const executablePath = resolveExecutablePath();
  const command = process.platform === 'win32' ? 'cmd.exe' : 'pnpm';
  const args = process.platform === 'win32'
    ? ['/c', 'pnpm', 'exec', 'playwright', 'test', '-c', 'playwright.desktop.config.ts', 'tests/e2e/desktop/electron-packaged-smoke.spec.ts', ...process.argv.slice(2)]
    : ['exec', 'playwright', 'test', '-c', 'playwright.desktop.config.ts', 'tests/e2e/desktop/electron-packaged-smoke.spec.ts', ...process.argv.slice(2)];

  const result = spawnSync(command, args, {
    cwd: path.join(__dirname, '..'),
    env: {
      ...process.env,
      ELECTRON_EXECUTABLE_PATH: executablePath,
    },
    stdio: 'inherit',
    shell: false,
  });

  if (result.error) {
    throw result.error;
  }

  process.exit(result.status ?? 1);
}

main();
