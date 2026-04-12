const fs = require('fs');
const path = require('path');
const { flipFuses, FuseVersion, FuseV1Options } = require('@electron/fuses');
const { RUNTIME_CONFIG_FILE, createDesktopRuntimeConfig } = require('./electron/runtime-config.cjs');
const packageJson = require('./package.json');

const updateUrl = (process.env.ELECTRON_UPDATES_URL || '').trim().replace(/\/$/, '');
const outputDir = (process.env.ELECTRON_BUILD_OUTPUT || '').trim() || packageJson.build.directories.output;
const allowUnsignedWindowsBuild = process.env.ELECTRON_ALLOW_UNSIGNED_WINDOWS_BUILD === 'true';
const publisherName = (process.env.ELECTRON_WINDOWS_PUBLISHER_NAME || '')
  .split('|')
  .map((value) => value.trim())
  .filter(Boolean);
const {
  publisherName: legacyPublisherName,
  signtoolOptions: existingSigntoolOptions = {},
  ...winBuildConfig
} = packageJson.build.win || {};
const resolvedPublisherName = publisherName.length > 0
  ? publisherName
  : (existingSigntoolOptions.publisherName || legacyPublisherName);

const publish = updateUrl
  ? [
      {
        provider: 'generic',
        url: updateUrl,
      },
    ]
  : undefined;

function getResourcesDirectory(appOutDir, electronPlatformName) {
  if (electronPlatformName === 'darwin') {
    return path.join(appOutDir, 'Contents', 'Resources');
  }

  return path.join(appOutDir, 'resources');
}

function syncStandaloneRuntime(context) {
  const standaloneSource = path.join(__dirname, '.next', 'standalone');

  if (!fs.existsSync(standaloneSource)) {
    throw new Error(`Missing standalone Next.js output at ${standaloneSource}. Run the web build before packaging.`);
  }

  const resourcesDir = getResourcesDirectory(context.appOutDir, context.electronPlatformName);
  const standaloneDestination = path.join(resourcesDir, 'standalone');

  fs.mkdirSync(standaloneDestination, { recursive: true });
  fs.cpSync(standaloneSource, standaloneDestination, { recursive: true, force: true });
}

function syncDesktopRuntimeConfig(context) {
  const resourcesDir = getResourcesDirectory(context.appOutDir, context.electronPlatformName);
  const runtimeConfigPath = path.join(resourcesDir, RUNTIME_CONFIG_FILE);
  const runtimeConfig = createDesktopRuntimeConfig(process.env, {
    allowLoopback: process.env.ELECTRON_ALLOW_LOCAL_API_TARGET === 'true',
  });

  if (Object.keys(runtimeConfig).length === 0) {
    if (fs.existsSync(runtimeConfigPath)) {
      fs.rmSync(runtimeConfigPath, { force: true });
    }
    return;
  }

  fs.mkdirSync(resourcesDir, { recursive: true });
  fs.writeFileSync(runtimeConfigPath, `${JSON.stringify(runtimeConfig, null, 2)}\n`, 'utf8');
}

module.exports = {
  ...packageJson.build,
  directories: {
    ...packageJson.build.directories,
    output: outputDir,
  },
  afterPack: async (context) => {
    syncStandaloneRuntime(context);
    syncDesktopRuntimeConfig(context);
  },
  afterSign: async (context) => {
    const electronBinaryPath = (() => {
      switch (context.electronPlatformName) {
        case 'darwin':
          return path.join(
            context.appOutDir,
            `${context.packager.appInfo.productFilename}.app`,
            'Contents', 'Frameworks', 'Electron Framework.framework',
            'Versions', 'A', 'Electron Framework',
          );
        case 'win32':
          return path.join(context.appOutDir, `${context.packager.appInfo.productFilename}.exe`);
        default:
          return path.join(context.appOutDir, context.packager.appInfo.productFilename);
      }
    })();

    try {
      await flipFuses(electronBinaryPath, {
        version: FuseVersion.V1,
        [FuseV1Options.RunAsNode]: false,
        [FuseV1Options.EnableNodeCliInspectArguments]: false,
        [FuseV1Options.EnableEmbeddedAsarIntegrityValidation]: true,
        [FuseV1Options.OnlyLoadAppFromAsar]: true,
      });
    } catch (fuseError) {
      console.error(`[electron-builder] Failed to flip fuses on ${electronBinaryPath}:`, fuseError);
      throw fuseError;
    }
  },
  publish,
  win: {
    ...winBuildConfig,
    signAndEditExecutable: allowUnsignedWindowsBuild
      ? false
      : winBuildConfig.signAndEditExecutable !== false,
    ...(resolvedPublisherName
      ? {
          signtoolOptions: {
            ...existingSigntoolOptions,
            publisherName: resolvedPublisherName,
          },
        }
      : {}),
  },
  mac: {
    target: ['dmg', 'zip'],
    hardenedRuntime: true,
    gatekeeperAssess: false,
  },
  linux: {
    target: ['AppImage', 'deb'],
    category: 'Education',
    mimeTypes: ['x-scheme-handler/oet-prep'],
  },
};
