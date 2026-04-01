const packageJson = require('./package.json');

const updateUrl = (process.env.ELECTRON_UPDATES_URL || '').trim().replace(/\/$/, '');
const outputDir = (process.env.ELECTRON_BUILD_OUTPUT || '').trim() || packageJson.build.directories.output;

const publish = updateUrl
  ? [
      {
        provider: 'generic',
        url: updateUrl,
      },
    ]
  : undefined;

module.exports = {
  ...packageJson.build,
  directories: {
    ...packageJson.build.directories,
    output: outputDir,
  },
  publish,
  mac: {
    target: ['dmg', 'zip'],
    hardenedRuntime: true,
    gatekeeperAssess: false,
  },
  linux: {
    target: ['AppImage', 'deb'],
    category: 'Education',
  },
};