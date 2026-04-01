const { app, BrowserWindow, ipcMain, shell, session, Menu, dialog } = require('electron');
const { spawn } = require('child_process');
const fs = require('fs');
const net = require('net');
const path = require('path');
const { createDesktopMenu } = require('./menu.cjs');
const { createDesktopUpdater } = require('./updater.cjs');

const DEFAULT_RENDERER_URL = 'http://localhost:3000';
const DEFAULT_PORT = Number.parseInt(process.env.PORT || '3000', 10) || 3000;
const DEFAULT_API_BASE_URL = (process.env.NEXT_PUBLIC_API_BASE_URL || '/api/backend').replace(/\/$/, '');
const DEFAULT_PROXY_TARGET_URL = (process.env.API_PROXY_TARGET_URL || 'http://localhost:5198').replace(/\/$/, '');
const START_LOCAL_RENDERER = process.env.ELECTRON_START_LOCAL_SERVER === 'true';
const ALLOWED_EXTERNAL_PROTOCOLS = new Set(['http:', 'https:']);
const ALLOWED_PERMISSION_NAMES = new Set(['media', 'microphone', 'notifications']);

if (!app.requestSingleInstanceLock()) {
  app.quit();
}

let mainWindow = null;
let rendererUrl = null;
let rendererServerProcess = null;
let desktopUpdater = null;
let updateCheckTimer = null;
let shuttingDown = false;

function getRendererUrl() {
  if (rendererUrl) {
    return rendererUrl;
  }

  return (process.env.ELECTRON_RENDERER_URL || DEFAULT_RENDERER_URL).replace(/\/$/, '');
}

function getRendererOrigin() {
  try {
    return new URL(getRendererUrl()).origin;
  } catch {
    return new URL(DEFAULT_RENDERER_URL).origin;
  }
}

function isTrustedRendererUrl(urlString) {
  try {
    const trustedOrigin = getRendererOrigin();
    const candidateOrigin = new URL(urlString).origin;
    return candidateOrigin === trustedOrigin;
  } catch {
    return false;
  }
}

function isAllowedExternalUrl(urlString) {
  try {
    return ALLOWED_EXTERNAL_PROTOCOLS.has(new URL(urlString).protocol);
  } catch {
    return false;
  }
}

function buildRendererUrl(routePath = '/') {
  const rendererUrlObject = new URL(getRendererUrl());
  const routeUrl = new URL(routePath, rendererUrlObject);

  rendererUrlObject.pathname = routeUrl.pathname;
  rendererUrlObject.search = routeUrl.search;
  rendererUrlObject.hash = routeUrl.hash;

  return rendererUrlObject.toString();
}

function mapProtocolUrlToRendererUrl(protocolUrl) {
  try {
    const parsedUrl = new URL(protocolUrl);
    const rendererUrlObject = new URL(getRendererUrl());
    const mappedPath = `/${parsedUrl.host}${parsedUrl.pathname}`.replace(/\/+/g, '/');

    rendererUrlObject.pathname = mappedPath === '/' ? '/' : mappedPath;
    rendererUrlObject.search = parsedUrl.search;
    rendererUrlObject.hash = parsedUrl.hash;

    return rendererUrlObject.toString();
  } catch {
    return null;
  }
}

function focusMainWindow() {
  if (!mainWindow) {
    return;
  }

  if (mainWindow.isMinimized()) {
    mainWindow.restore();
  }

  mainWindow.focus();
}

function reloadCurrentWindow() {
  if (!mainWindow) {
    return;
  }

  mainWindow.reload();
  focusMainWindow();
}

function openExternalUrl(urlString) {
  if (!isAllowedExternalUrl(urlString)) {
    return false;
  }

  void shell.openExternal(urlString);
  return true;
}

function navigateToRoute(routePath) {
  if (!mainWindow) {
    return;
  }

  void mainWindow.loadURL(buildRendererUrl(routePath));
  focusMainWindow();
}

function navigateToUrl(urlString) {
  if (!mainWindow) {
    return;
  }

  if (isTrustedRendererUrl(urlString)) {
    void mainWindow.loadURL(urlString);
    focusMainWindow();
    return;
  }

  if (openExternalUrl(urlString)) {
    return;
  }
}

function openOrNavigateUrl(urlString) {
  if (typeof urlString !== 'string' || urlString.trim() === '') {
    return;
  }

  if (urlString.startsWith('oet-prep://')) {
    const mappedUrl = mapProtocolUrlToRendererUrl(urlString);
    if (mappedUrl) {
      navigateToUrl(mappedUrl);
      return;
    }
  }

  navigateToUrl(urlString);
}

function canRequestPermission(webContents, permission, requestingOrigin) {
  if (!ALLOWED_PERMISSION_NAMES.has(permission)) {
    return false;
  }

  const contentsUrl = webContents?.getURL?.() || requestingOrigin || '';
  return isTrustedRendererUrl(contentsUrl) || requestingOrigin === getRendererOrigin();
}

function applyWebContentsSecurityPolicies(webContents) {
  webContents.setWindowOpenHandler(({ url }) => {
    openOrNavigateUrl(url);
    return { action: 'deny' };
  });

  webContents.on('will-navigate', (event, url) => {
    if (!isTrustedRendererUrl(url)) {
      event.preventDefault();
      openOrNavigateUrl(url);
    }
  });

  webContents.on('will-attach-webview', (event) => {
    event.preventDefault();
  });
}

function applyApplicationSecurityPolicies() {
  session.defaultSession.setPermissionCheckHandler((webContents, permission, requestingOrigin) => {
    return canRequestPermission(webContents, permission, requestingOrigin);
  });

  session.defaultSession.setPermissionRequestHandler((webContents, permission, callback, details) => {
    const requestingOrigin = details?.requestingOrigin || details?.securityOrigin || '';
    callback(canRequestPermission(webContents, permission, requestingOrigin));
  });

  app.on('web-contents-created', (_event, contents) => {
    applyWebContentsSecurityPolicies(contents);
  });
}

function wait(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

async function waitForRenderer(url) {
  const healthUrl = new URL('/api/health', url).toString();

  for (let attempt = 0; attempt < 120; attempt += 1) {
    try {
      const response = await fetch(healthUrl, { cache: 'no-store' });
      if (response.ok) {
        return;
      }
    } catch {
      // keep waiting
    }

    await wait(1000);
  }

  throw new Error(`Timed out waiting for the renderer at ${healthUrl}`);
}

function isPortAvailable(port) {
  return new Promise((resolve) => {
    const server = net.createServer();

    server.unref();
    server.once('error', () => {
      resolve(false);
    });
    server.once('listening', () => {
      server.close(() => resolve(true));
    });
    server.listen(port);
  });
}

async function findAvailablePort(startPort) {
  for (let port = startPort; port < startPort + 25; port += 1) {
    if (await isPortAvailable(port)) {
      return port;
    }
  }

  throw new Error(`Unable to find an open port starting from ${startPort}`);
}

function getStandaloneRoot() {
  if (app.isPackaged) {
    return path.join(process.resourcesPath, 'standalone');
  }

  return path.join(app.getAppPath(), '.next', 'standalone');
}

function getStandaloneServerPath() {
  return path.join(getStandaloneRoot(), 'server.js');
}

function stopRendererServer() {
  if (rendererServerProcess && !rendererServerProcess.killed) {
    rendererServerProcess.kill();
  }

  rendererServerProcess = null;
}

function stopUpdateCheckTimer() {
  if (updateCheckTimer) {
    clearTimeout(updateCheckTimer);
    updateCheckTimer = null;
  }
}

function getStandaloneServerEnv(runtimeUrl, port) {
  return {
    ...process.env,
    ELECTRON_RUN_AS_NODE: '1',
    NODE_ENV: 'production',
    PORT: String(port),
    HOSTNAME: '127.0.0.1',
    NEXT_PUBLIC_API_BASE_URL: DEFAULT_API_BASE_URL,
    API_PROXY_TARGET_URL: DEFAULT_PROXY_TARGET_URL,
    APP_URL: runtimeUrl,
  };
}

async function startStandaloneRendererServer() {
  const standaloneRoot = getStandaloneRoot();
  const serverScript = getStandaloneServerPath();

  if (!fs.existsSync(serverScript)) {
    throw new Error(`Packaged renderer assets are missing. Expected ${serverScript}.`);
  }

  const port = await findAvailablePort(DEFAULT_PORT);
  const runtimeUrl = `http://127.0.0.1:${port}`;

  rendererServerProcess = spawn(process.execPath, [serverScript], {
    cwd: standaloneRoot,
    env: getStandaloneServerEnv(runtimeUrl, port),
    stdio: 'inherit',
    shell: false,
    windowsHide: true,
  });

  rendererServerProcess.on('exit', (code, signal) => {
    rendererServerProcess = null;

    if (!shuttingDown) {
      console.error('[electron] standalone renderer exited unexpectedly', { code, signal });
      app.quit();
    }
  });

  await waitForRenderer(runtimeUrl);
  return runtimeUrl;
}

async function resolveRendererUrl() {
  const explicitRendererUrl = process.env.ELECTRON_RENDERER_URL?.trim();

  if (explicitRendererUrl) {
    rendererUrl = explicitRendererUrl.replace(/\/$/, '');
    return rendererUrl;
  }

  if (app.isPackaged || START_LOCAL_RENDERER) {
    rendererUrl = await startStandaloneRendererServer();
    return rendererUrl;
  }

  rendererUrl = DEFAULT_RENDERER_URL;
  return rendererUrl;
}

async function createWindow() {
  mainWindow = new BrowserWindow({
    width: 1440,
    height: 980,
    minWidth: 1200,
    minHeight: 800,
    backgroundColor: '#f6f8fb',
    title: 'OET Prep',
    show: false,
    webPreferences: {
      preload: path.join(__dirname, 'preload.cjs'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
      webSecurity: true,
      allowRunningInsecureContent: false,
      webviewTag: false,
      spellcheck: true,
    },
  });

  mainWindow.setMenuBarVisibility(true);
  applyWebContentsSecurityPolicies(mainWindow.webContents);

  mainWindow.on('closed', () => {
    mainWindow = null;
  });

  mainWindow.once('ready-to-show', () => {
    mainWindow?.show();
  });

  mainWindow.webContents.on('did-fail-load', (_event, errorCode, errorDescription, validatedURL) => {
    if (!mainWindow || errorCode === -3) {
      return;
    }

    if (process.env.NODE_ENV !== 'production') {
      console.error('[electron] failed to load renderer', { errorCode, errorDescription, validatedURL });
    }
  });

  await mainWindow.loadURL(getRendererUrl());
}

function refreshApplicationMenu() {
  Menu.setApplicationMenu(createDesktopMenu({
    isDev: !app.isPackaged,
    updateState: desktopUpdater?.getState?.() || {
      enabled: false,
      status: 'idle',
      available: false,
      downloaded: false,
      version: null,
      progress: null,
      error: null,
    },
    onNavigate: navigateToRoute,
    onCheckForUpdates: async () => {
      await desktopUpdater?.checkForUpdates?.();
      refreshApplicationMenu();
    },
    onInstallUpdate: () => {
      desktopUpdater?.installDownloadedUpdate?.();
    },
    onShowAbout: async () => {
      const parentWindow = mainWindow || BrowserWindow.getFocusedWindow() || undefined;
      await dialog.showMessageBox(parentWindow, {
        type: 'info',
        buttons: ['OK'],
        defaultId: 0,
        title: 'About OET Prep',
        message: 'OET Prep',
        detail: `Version ${app.getVersion()}\nElectron ${process.versions.electron}\nNode ${process.versions.node}`,
      });
    },
  }));
}

function scheduleUpdateCheck() {
  stopUpdateCheckTimer();

  if (!desktopUpdater?.isEnabled?.()) {
    return;
  }

  updateCheckTimer = setTimeout(() => {
    void desktopUpdater.checkForUpdates().finally(() => {
      refreshApplicationMenu();
    });
  }, 15000);

  if (typeof updateCheckTimer.unref === 'function') {
    updateCheckTimer.unref();
  }
}

app.on('second-instance', (_event, argv) => {
  const protocolUrl = argv.find((argument) => argument.startsWith('oet-prep://'));
  if (protocolUrl) {
    openOrNavigateUrl(protocolUrl);
  }

  focusMainWindow();
});

app.on('open-url', (event, url) => {
  event.preventDefault();
  openOrNavigateUrl(url);
});

app.whenReady().then(async () => {
  try {
    app.setAppUserModelId('com.oetprep.desktop');
    applyApplicationSecurityPolicies();

    if (app.isPackaged) {
      app.setAsDefaultProtocolClient('oet-prep');
    }

    desktopUpdater = createDesktopUpdater({
      getWindow: () => mainWindow,
      onStateChange: () => refreshApplicationMenu(),
    });

    await resolveRendererUrl();
    await createWindow();
    refreshApplicationMenu();
    scheduleUpdateCheck();

    app.on('activate', () => {
      if (BrowserWindow.getAllWindows().length === 0) {
        void createWindow();
      } else {
        focusMainWindow();
      }
    });
  } catch (error) {
    console.error('[electron] failed to start desktop shell', error);
    app.quit();
  }
});

app.on('before-quit', () => {
  shuttingDown = true;
  stopUpdateCheckTimer();
  stopRendererServer();
});

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') {
    app.quit();
  }
});

ipcMain.handle('desktop:open-external', async (_event, url) => {
  if (typeof url !== 'string' || url.trim() === '') {
    throw new Error('A valid URL is required.');
  }

  if (!isAllowedExternalUrl(url)) {
    throw new Error('Only http and https URLs can be opened externally.');
  }

  await shell.openExternal(url);
  return true;
});
