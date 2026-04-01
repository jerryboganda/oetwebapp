const { spawn } = require('child_process');
const fs = require('fs');
const os = require('os');
const path = require('path');

const npmCommand = process.platform === 'win32' ? 'cmd.exe' : 'npm';
const electronBuilderConfigPath = path.relative(process.cwd(), path.join(__dirname, '..', 'electron-builder.config.cjs'));
const publishMode = process.env.ELECTRON_PUBLISH_MODE || 'never';
const workspaceOutputDir = path.join(__dirname, '..', 'dist', 'desktop');
const electronBuildOutputDir = fs.mkdtempSync(path.join(os.tmpdir(), 'oet-prep-electron-'));

function npmArgs(args) {
  return process.platform === 'win32' ? ['/c', 'npm', ...args] : args;
}

function run(command, args, options = {}) {
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, {
      stdio: 'inherit',
      shell: false,
      env: options.env || process.env,
    });

    child.on('error', reject);
    child.on('exit', (code, signal) => {
      if (code === 0) {
        resolve();
        return;
      }

      reject(new Error(`Command failed with exit code ${code ?? 'unknown'}${signal ? ` and signal ${signal}` : ''}.`));
    });
  });
}

function cleanupWorkspaceArtifacts(outputDir) {
  if (!fs.existsSync(outputDir)) {
    return;
  }

  for (const entry of fs.readdirSync(outputDir, { withFileTypes: true })) {
    if (!entry.isFile()) {
      continue;
    }

    if (!/\.(exe|yml|blockmap)$/i.test(entry.name)) {
      continue;
    }

    try {
      fs.unlinkSync(path.join(outputDir, entry.name));
    } catch (error) {
      console.warn(`[desktop-dist] could not remove ${entry.name} from workspace output`, error);
    }
  }
}

function copyBuildArtifacts(sourceDir, destinationDir) {
  if (!fs.existsSync(sourceDir)) {
    throw new Error(`Expected Electron build output at ${sourceDir}, but it was not created.`);
  }

  fs.mkdirSync(destinationDir, { recursive: true });

  for (const entry of fs.readdirSync(sourceDir, { withFileTypes: true })) {
    if (!entry.isFile()) {
      continue;
    }

    if (!/\.(exe|yml|blockmap)$/i.test(entry.name)) {
      continue;
    }

    fs.copyFileSync(path.join(sourceDir, entry.name), path.join(destinationDir, entry.name));
  }
}

async function main() {
  const buildEnv = {
    ...process.env,
    NODE_ENV: 'production',
    NEXT_TELEMETRY_DISABLED: '1',
    NEXT_PUBLIC_API_BASE_URL: process.env.NEXT_PUBLIC_API_BASE_URL || '/api/backend',
    APP_URL: process.env.APP_URL || 'http://localhost:3000',
  };
  const electronBuilderEnv = {
    ...buildEnv,
    ELECTRON_BUILD_OUTPUT: electronBuildOutputDir,
  };

  await run(npmCommand, npmArgs(['run', 'build']), { env: buildEnv });
  try {
    await run(npmCommand, npmArgs(['exec', '--', 'electron-builder', '--config', electronBuilderConfigPath, '--publish', publishMode]), { env: electronBuilderEnv });
    cleanupWorkspaceArtifacts(workspaceOutputDir);
    copyBuildArtifacts(electronBuildOutputDir, workspaceOutputDir);
  } finally {
    try {
      fs.rmSync(electronBuildOutputDir, { recursive: true, force: true });
    } catch {
      // Ignore temp cleanup failures.
    }
  }
}

main().catch((error) => {
  console.error('[desktop-dist] packaging failed', error);
  process.exit(1);
});
