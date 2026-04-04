const { spawn } = require('child_process');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { loadEnvConfig } = require('@next/env');
const { loadCertificatePinningConfig, matchesPinnedHost } = require('../electron/security/certificate-pinning.cjs');

const npmCommand = process.platform === 'win32' ? 'cmd.exe' : 'npm';
const electronBuilderConfigPath = path.relative(process.cwd(), path.join(__dirname, '..', 'electron-builder.config.cjs'));
const publishMode = process.env.ELECTRON_PUBLISH_MODE || 'never';
const workspaceOutputDir = path.join(__dirname, '..', 'dist', 'desktop');
const backendRuntimeDir = path.join(__dirname, '..', 'desktop-backend-runtime');
const electronBuildOutputDir = fs.mkdtempSync(path.join(os.tmpdir(), 'oet-prep-electron-'));

function npmArgs(args) {
  return process.platform === 'win32' ? ['/c', 'npm', ...args] : args;
}

function isAbsoluteHttpUrl(value) {
  try {
    const parsed = new URL(value);
    return parsed.protocol === 'https:' || parsed.protocol === 'http:';
  } catch {
    return false;
  }
}

function isLocalHostname(hostname) {
  return hostname === 'localhost' || hostname === '127.0.0.1' || hostname === '::1';
}

function collectRequiredPinnedHosts(environment) {
  const hosts = new Set();
  const candidateUrls = [
    environment.ELECTRON_UPDATES_URL,
    environment.NEXT_PUBLIC_API_BASE_URL,
    environment.PUBLIC_API_BASE_URL,
  ];

  for (const value of candidateUrls) {
    if (!value || !isAbsoluteHttpUrl(value)) {
      continue;
    }

    const parsed = new URL(value);

    if (parsed.protocol !== 'https:' || isLocalHostname(parsed.hostname)) {
      continue;
    }

    hosts.add(parsed.hostname.toLowerCase());
  }

  return [...hosts];
}

function hasWindowsSigningConfiguration(environment) {
  const hasClassicCertificate = Boolean(
    (environment.WIN_CSC_LINK && environment.WIN_CSC_KEY_PASSWORD)
      || (environment.CSC_LINK && environment.CSC_KEY_PASSWORD),
  );
  const hasTrustedSigning = Boolean(
    environment.AZURE_TENANT_ID
      && environment.AZURE_CLIENT_ID
      && environment.AZURE_CLIENT_SECRET
      && environment.AZURE_CODE_SIGNING_ACCOUNT_NAME
      && environment.AZURE_CODE_SIGNING_PROFILE_NAME,
  );

  return hasClassicCertificate || hasTrustedSigning;
}

function validateDesktopReleaseSecurity(environment) {
  if (publishMode !== 'never' && !environment.ELECTRON_UPDATES_URL) {
    throw new Error('ELECTRON_UPDATES_URL is required when ELECTRON_PUBLISH_MODE is not "never".');
  }

  const requiredPinnedHosts = collectRequiredPinnedHosts(environment);
  const certificatePinRules = loadCertificatePinningConfig(environment.ELECTRON_CERT_PINS);

  if (requiredPinnedHosts.length > 0 && certificatePinRules.length === 0) {
    throw new Error(`ELECTRON_CERT_PINS must define pins for these HTTPS hosts: ${requiredPinnedHosts.join(', ')}`);
  }

  const missingPinnedHosts = requiredPinnedHosts.filter((hostname) => {
    return !certificatePinRules.some((rule) => matchesPinnedHost(rule, hostname));
  });

  if (missingPinnedHosts.length > 0) {
    throw new Error(`ELECTRON_CERT_PINS is missing certificate pins for: ${missingPinnedHosts.join(', ')}`);
  }

  if (
    process.platform === 'win32'
    && environment.ELECTRON_ALLOW_UNSIGNED_WINDOWS_BUILD !== 'true'
    && !hasWindowsSigningConfiguration(environment)
  ) {
    throw new Error(
      'Windows desktop packaging requires code-signing material. Configure WIN_CSC_LINK/WIN_CSC_KEY_PASSWORD, CSC_LINK/CSC_KEY_PASSWORD, or Azure Trusted Signing env vars. For local throwaway builds only, set ELECTRON_ALLOW_UNSIGNED_WINDOWS_BUILD=true.',
    );
  }
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
    if (entry.isDirectory() && /-unpacked$/i.test(entry.name)) {
      try {
        fs.rmSync(path.join(outputDir, entry.name), { recursive: true, force: true });
      } catch (error) {
        console.warn(`[desktop-dist] could not remove ${entry.name} from workspace output`, error);
      }
      continue;
    }

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
    if (entry.isDirectory() && /-unpacked$/i.test(entry.name)) {
      fs.cpSync(
        path.join(sourceDir, entry.name),
        path.join(destinationDir, entry.name),
        { recursive: true, force: true },
      );
      continue;
    }

    if (!entry.isFile()) {
      continue;
    }

    if (!/\.(exe|yml|blockmap)$/i.test(entry.name)) {
      continue;
    }

    fs.copyFileSync(path.join(sourceDir, entry.name), path.join(destinationDir, entry.name));
  }
}

function publishDesktopBackend(outputDir) {
  return run('dotnet', [
    'publish',
    'backend/src/OetLearner.Api/OetLearner.Api.csproj',
    '-c', 'Release',
    '-r', 'win-x64',
    '--self-contained', 'true',
    '-o', outputDir,
  ]);
}

async function main() {
  loadEnvConfig(process.cwd(), false, console);

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

  validateDesktopReleaseSecurity(electronBuilderEnv);
  fs.rmSync(backendRuntimeDir, { recursive: true, force: true });

  await publishDesktopBackend(backendRuntimeDir);
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
