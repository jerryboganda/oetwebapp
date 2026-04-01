const fs = require('fs');
const path = require('path');
const { app, dialog, Notification } = require('electron');
const { autoUpdater } = require('electron-updater');

function getAppUpdateMetadataPath() {
  return path.join(process.resourcesPath, 'app-update.yml');
}

function isUpdaterConfigured() {
  return app.isPackaged && fs.existsSync(getAppUpdateMetadataPath());
}

function createUpdateState() {
  return {
    enabled: isUpdaterConfigured(),
    status: 'idle',
    available: false,
    downloaded: false,
    version: null,
    progress: null,
    error: null,
  };
}

function createDesktopUpdater({ getWindow, onStateChange }) {
  let state = createUpdateState();
  let isDownloading = false;

  function emitState(patch) {
    state = {
      ...state,
      ...patch,
    };

    const mainWindow = getWindow?.();
    if (mainWindow) {
      if (patch.progress) {
        mainWindow.setProgressBar(Math.max(0, Math.min(1, patch.progress.percent / 100)));
      } else if (patch.status === 'idle' || patch.status === 'up-to-date' || patch.status === 'error' || patch.status === 'downloaded') {
        mainWindow.setProgressBar(-1);
      }
    }

    onStateChange?.(state);
  }

  function notify(title, body, onClick) {
    if (Notification.isSupported()) {
      const notification = new Notification({
        title,
        body,
      });

      if (typeof onClick === 'function') {
        notification.on('click', onClick);
      }

      notification.show();
      return;
    }

    void dialog.showMessageBox({
      type: 'info',
      buttons: typeof onClick === 'function' ? ['Restart now', 'Later'] : ['OK'],
      defaultId: 0,
      cancelId: typeof onClick === 'function' ? 1 : 0,
      title,
      message: body,
    }).then((result) => {
      if (typeof onClick === 'function' && result.response === 0) {
        onClick();
      }
    });
  }

  async function downloadUpdate() {
    if (!state.enabled || isDownloading) {
      return false;
    }

    isDownloading = true;
    emitState({ status: 'downloading', error: null });

    try {
      await autoUpdater.downloadUpdate();
      return true;
    } catch (error) {
      emitState({
        status: 'error',
        error: error instanceof Error ? error.message : 'Failed to download update.',
      });
      throw error;
    } finally {
      isDownloading = false;
    }
  }

  autoUpdater.autoDownload = false;
  autoUpdater.autoInstallOnAppQuit = true;
  autoUpdater.allowDowngrade = false;
  autoUpdater.logger = console;

  autoUpdater.on('checking-for-update', () => {
    emitState({
      status: 'checking',
      error: null,
    });
  });

  autoUpdater.on('update-not-available', (info) => {
    emitState({
      status: 'up-to-date',
      available: false,
      downloaded: false,
      version: info?.version ?? null,
      progress: null,
      error: null,
    });
  });

  autoUpdater.on('update-available', (info) => {
    emitState({
      status: 'available',
      available: true,
      downloaded: false,
      version: info?.version ?? null,
      progress: null,
      error: null,
    });

    notify(
      'Update available',
      info?.version
        ? `Downloading OET Prep ${info.version} in the background.`
        : 'Downloading the latest OET Prep build in the background.',
    );

    void downloadUpdate().catch((error) => {
      console.error('[electron] update download failed', error);
    });
  });

  autoUpdater.on('download-progress', (progress) => {
    emitState({
      status: 'downloading',
      progress,
    });
  });

  autoUpdater.on('update-downloaded', (info) => {
    emitState({
      status: 'downloaded',
      available: true,
      downloaded: true,
      version: info?.version ?? state.version,
      progress: null,
      error: null,
    });

    notify(
      'Update ready',
      info?.version
        ? `Restart OET Prep to install version ${info.version}.`
        : 'Restart OET Prep to install the downloaded update.',
      () => {
        autoUpdater.quitAndInstall(false, true);
      },
    );
  });

  autoUpdater.on('error', (error) => {
    emitState({
      status: 'error',
      error: error instanceof Error ? error.message : 'Auto-updater failed.',
    });
  });

  async function checkForUpdates() {
    if (!state.enabled) {
      return {
        enabled: false,
        state,
      };
    }

    try {
      const result = await autoUpdater.checkForUpdates();
      return {
        enabled: true,
        state,
        result,
      };
    } catch (error) {
      emitState({
        status: 'error',
        error: error instanceof Error ? error.message : 'Unable to check for updates.',
      });

      return {
        enabled: true,
        state,
        error,
      };
    }
  }

  function installDownloadedUpdate() {
    if (!state.downloaded) {
      return false;
    }

    autoUpdater.quitAndInstall(false, true);
    return true;
  }

  function getState() {
    return state;
  }

  return {
    checkForUpdates,
    downloadUpdate,
    getState,
    installDownloadedUpdate,
    isEnabled: () => state.enabled,
  };
}

module.exports = {
  createDesktopUpdater,
  isUpdaterConfigured,
};