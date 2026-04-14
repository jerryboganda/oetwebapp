const { app, BrowserWindow, ipcMain, shell, session, Menu, dialog, Tray, Notification, nativeImage } = require('electron');
const { spawn } = require('child_process');
const fs = require('fs');
const net = require('net');
const path = require('path');
const { createDesktopMenu } = require('./menu.cjs');
const { installCertificatePinning } = require('./security/certificate-pinning.cjs');
const { createSecureSecretStore } = require('./security/secure-secrets.cjs');
const { loadDesktopRuntimeConfig, resolveDesktopApiBaseUrl } = require('./runtime-config.cjs');
const { createDesktopUpdater } = require('./updater.cjs');

const DEFAULT_RENDERER_URL = 'http://localhost:3000';
const DEFAULT_PORT = Number.parseInt(process.env.PORT || '3000', 10) || 3000;
const DEFAULT_API_BASE_URL = (process.env.NEXT_PUBLIC_API_BASE_URL || '/api/backend').replace(/\/$/, '');
const DEFAULT_PROXY_TARGET_URL = (process.env.API_PROXY_TARGET_URL || 'http://localhost:5198').replace(/\/$/, '');
const START_LOCAL_RENDERER = process.env.ELECTRON_START_LOCAL_SERVER === 'true';
const ALLOWED_EXTERNAL_PROTOCOLS = new Set(['http:', 'https:']);
const ALLOWED_PERMISSION_NAMES = new Set(['media', 'microphone', 'notifications']);
const BUNDLED_BACKEND_DIR = 'backend-runtime';
const BUNDLED_BACKEND_EXECUTABLE = process.platform === 'win32' ? 'OetLearner.Api.exe' : 'OetLearner.Api';

function resolveRuntimeChannel() {
  const explicitChannel = process.env.ELECTRON_RUNTIME_CHANNEL?.trim();
  if (explicitChannel) {
    return explicitChannel.replace(/[^a-zA-Z0-9_-]+/g, '-').toLowerCase();
  }

  return app.isPackaged ? 'prod' : 'dev';
}

function configureDesktopDataPaths() {
  const channel = resolveRuntimeChannel();
  const explicitRoot = process.env.ELECTRON_APPDATA_ROOT?.trim();
  const baseDataRoot = explicitRoot
    ? path.resolve(explicitRoot)
    : path.join(app.getPath('appData'), 'OET Prep');
  const channelRoot = path.join(baseDataRoot, channel);
  const userDataPath = path.join(channelRoot, 'user-data');
  const sessionDataPath = path.join(channelRoot, 'session-data');

  fs.mkdirSync(userDataPath, { recursive: true });
  fs.mkdirSync(sessionDataPath, { recursive: true });

  app.setPath('userData', userDataPath);

  try {
    app.setPath('sessionData', sessionDataPath);
  } catch {
    // Older Electron builds may not expose sessionData; userData still isolates the profile.
  }

  return { channel, userDataPath, sessionDataPath };
}

const desktopDataPaths = configureDesktopDataPaths();

if (!app.requestSingleInstanceLock()) {
  app.quit();
}

let mainWindow = null;
let rendererUrl = null;
let rendererServerProcess = null;
let backendServerProcess = null;
let desktopUpdater = null;
let updateCheckTimer = null;
let shuttingDown = false;
let secureSecretStore = null;
let desktopRuntimeConfig = null;
let tray = null;
const allowPackagedLoopbackApiTarget = !app.isPackaged || process.env.ELECTRON_ALLOW_LOCAL_API_TARGET === 'true';
let activeBackendUrl = null;
let ignoredPackagedLoopbackApiTarget = null;

function getMainWindowState() {
  if (!mainWindow) {
    return {
      isFocused: false,
      isVisible: false,
      isMinimized: false,
      isMaximized: false,
      isFullScreen: false,
    };
  }

  return {
    isFocused: mainWindow.isFocused(),
    isVisible: mainWindow.isVisible(),
    isMinimized: mainWindow.isMinimized(),
    isMaximized: mainWindow.isMaximized(),
    isFullScreen: mainWindow.isFullScreen(),
  };
}

function broadcastMainWindowState() {
  if (!mainWindow || mainWindow.isDestroyed()) {
    return;
  }

  mainWindow.webContents.send('desktop:window-state-changed', getMainWindowState());
}

function getDesktopRuntimeConfig() {
  if (desktopRuntimeConfig) {
    return desktopRuntimeConfig;
  }

  desktopRuntimeConfig = loadDesktopRuntimeConfig({
    resourcesPath: process.resourcesPath,
    appPath: app.getAppPath(),
    userDataPath: app.getPath('userData'),
    isPackaged: app.isPackaged,
    logger: console,
  });

  return desktopRuntimeConfig;
}

function getConfiguredDesktopApiBaseUrl() {
  const runtimeConfig = getDesktopRuntimeConfig();
  const resolvedUrl = resolveDesktopApiBaseUrl(
    process.env,
    runtimeConfig,
    { allowLoopback: allowPackagedLoopbackApiTarget },
  );

  if (
    app.isPackaged
    && !allowPackagedLoopbackApiTarget
    && runtimeConfig.publicApiBaseUrl
    && !resolvedUrl
  ) {
    ignoredPackagedLoopbackApiTarget = runtimeConfig.publicApiBaseUrl;
    console.warn('[electron] ignoring packaged desktop api target because it points to a loopback address', {
      publicApiBaseUrl: runtimeConfig.publicApiBaseUrl,
    });
  }

  return resolvedUrl;
}

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

function validateSenderFrame(event) {
  const senderUrl = event?.senderFrame?.url;
  if (!senderUrl) {
    return false;
  }
  return isTrustedRendererUrl(senderUrl);
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
  broadcastMainWindowState();
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

function formatStartupError(error) {
  if (error instanceof Error && error.message.trim()) {
    return error.message;
  }

  if (typeof error === 'string' && error.trim()) {
    return error;
  }

  return 'Unknown startup failure.';
}

function showStartupFailure(error) {
  console.error('[electron] failed to start desktop shell', error);
  dialog.showErrorBox('OET Prep failed to start', formatStartupError(error));
}

function normalizeSecretNamespace(value) {
  if (typeof value !== 'string' || value.trim() === '') {
    throw new Error('A non-empty secret namespace is required.');
  }

  return value.trim();
}

function normalizeSecretKey(value) {
  if (typeof value !== 'string' || value.trim() === '') {
    throw new Error('A non-empty secret key is required.');
  }

  return value.trim();
}

function normalizeSecretValue(value) {
  if (typeof value !== 'string') {
    throw new Error('Secret values must be strings.');
  }

  return value;
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

async function waitForBackend(url) {
  const healthUrl = new URL('/health/ready', url).toString();

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

  throw new Error(`Timed out waiting for the backend at ${healthUrl}`);
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

function getBundledBackendRoot() {
  if (app.isPackaged) {
    return path.join(process.resourcesPath, BUNDLED_BACKEND_DIR);
  }

  return path.join(app.getAppPath(), 'dist', BUNDLED_BACKEND_DIR);
}

function getBundledBackendExecutablePath() {
  return path.join(getBundledBackendRoot(), BUNDLED_BACKEND_EXECUTABLE);
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

function stopBackendServer() {
  if (backendServerProcess && !backendServerProcess.killed) {
    backendServerProcess.kill();
  }

  backendServerProcess = null;
}

function stopUpdateCheckTimer() {
  if (updateCheckTimer) {
    clearTimeout(updateCheckTimer);
    updateCheckTimer = null;
  }
}

function getStandaloneServerEnv(runtimeUrl, port, backendUrl) {
  return {
    ...process.env,
    ELECTRON_RUN_AS_NODE: '1',
    NODE_ENV: 'production',
    PORT: String(port),
    HOSTNAME: '127.0.0.1',
    NEXT_PUBLIC_API_BASE_URL: DEFAULT_API_BASE_URL,
    API_PROXY_TARGET_URL: backendUrl || DEFAULT_PROXY_TARGET_URL,
    APP_URL: runtimeUrl,
  };
}

function getBundledBackendEnv(runtimeUrl) {
  const dataRoot = path.join(app.getPath('userData'), 'backend');
  const storageRoot = path.join(app.getPath('userData'), 'storage');
  const databasePath = path.join(dataRoot, 'oet-prep.desktop.db');
  const isProduction = app.isPackaged;

  fs.mkdirSync(dataRoot, { recursive: true });
  fs.mkdirSync(storageRoot, { recursive: true });

  return {
    ...process.env,
    ASPNETCORE_ENVIRONMENT: isProduction ? 'Production' : 'Development',
    ASPNETCORE_URLS: runtimeUrl,
    ConnectionStrings__DefaultConnection: `Data Source=${databasePath}`,
    Auth__UseDevelopmentAuth: isProduction ? 'false' : 'true',
    Bootstrap__AutoMigrate: 'true',
    Bootstrap__SeedDemoData: isProduction ? 'false' : 'true',
    Platform__PublicApiBaseUrl: runtimeUrl,
    Platform__PublicWebBaseUrl: DEFAULT_RENDERER_URL,
    Billing__CheckoutBaseUrl: `${DEFAULT_RENDERER_URL}/billing/checkout`,
    Proxy__TrustForwardHeaders: 'false',
    Proxy__EnforceHttps: 'false',
    Storage__LocalRootPath: storageRoot,
  };
}

async function startBundledBackendServer() {
  const backendExecutable = getBundledBackendExecutablePath();

  if (!fs.existsSync(backendExecutable)) {
    throw new Error(`Bundled backend executable is missing. Expected ${backendExecutable}.`);
  }

  const port = await findAvailablePort(Number.parseInt(process.env.ELECTRON_BACKEND_PORT || '5198', 10) || 5198);
  const runtimeUrl = `http://127.0.0.1:${port}`;

  backendServerProcess = spawn(backendExecutable, [], {
    cwd: getBundledBackendRoot(),
    env: getBundledBackendEnv(runtimeUrl),
    stdio: 'inherit',
    shell: false,
    windowsHide: true,
  });

  backendServerProcess.on('exit', (code, signal) => {
    backendServerProcess = null;

    if (!shuttingDown) {
      showStartupFailure(new Error(`[electron] bundled backend exited unexpectedly (code ${code ?? 'unknown'}${signal ? `, signal ${signal}` : ''}).`));
      app.quit();
    }
  });

  await waitForBackend(runtimeUrl);
  activeBackendUrl = runtimeUrl;
  return runtimeUrl;
}

async function startStandaloneRendererServer(backendUrl) {
  const standaloneRoot = getStandaloneRoot();
  const serverScript = getStandaloneServerPath();

  if (!fs.existsSync(serverScript)) {
    throw new Error(`Packaged renderer assets are missing. Expected ${serverScript}.`);
  }

  const port = await findAvailablePort(DEFAULT_PORT);
  const runtimeUrl = `http://127.0.0.1:${port}`;

  rendererServerProcess = spawn(process.execPath, [serverScript], {
    cwd: standaloneRoot,
    env: getStandaloneServerEnv(runtimeUrl, port, backendUrl),
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
    const backendUrl = getConfiguredDesktopApiBaseUrl() || await startBundledBackendServer();
    activeBackendUrl = backendUrl;
    rendererUrl = await startStandaloneRendererServer(backendUrl);
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
      backgroundThrottling: true,
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

  const syncWindowState = () => {
    broadcastMainWindowState();
  };

  mainWindow.on('show', syncWindowState);
  mainWindow.on('hide', syncWindowState);
  mainWindow.on('focus', syncWindowState);
  mainWindow.on('blur', syncWindowState);
  mainWindow.on('minimize', syncWindowState);
  mainWindow.on('restore', syncWindowState);
  mainWindow.on('maximize', syncWindowState);
  mainWindow.on('unmaximize', syncWindowState);
  mainWindow.on('enter-full-screen', syncWindowState);
  mainWindow.on('leave-full-screen', syncWindowState);

  mainWindow.once('ready-to-show', () => {
    mainWindow?.show();
    broadcastMainWindowState();
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
  broadcastMainWindowState();
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

// ── System Tray ─────────────────────────────────────────────────────

function createSystemTray() {
  if (tray) return;

  const iconPath = path.join(__dirname, '..', 'public', 'icon.svg');
  let trayIcon;

  try {
    trayIcon = nativeImage.createFromPath(iconPath).resize({ width: 16, height: 16 });
  } catch {
    trayIcon = nativeImage.createEmpty();
  }

  tray = new Tray(trayIcon);
  tray.setToolTip('OET Prep');

  const contextMenu = Menu.buildFromTemplate([
    {
      label: 'Open OET Prep',
      click: () => focusMainWindow(),
    },
    {
      label: 'Dashboard',
      click: () => { focusMainWindow(); navigateToRoute('/dashboard'); },
    },
    {
      label: 'Study Plan',
      click: () => { focusMainWindow(); navigateToRoute('/study-plan'); },
    },
    { type: 'separator' },
    {
      label: 'Quit',
      click: () => app.quit(),
    },
  ]);

  tray.setContextMenu(contextMenu);

  tray.on('click', () => {
    focusMainWindow();
  });
}

// ── Desktop Native Notifications ────────────────────────────────────

function showDesktopNotification(title, body, route) {
  if (!Notification.isSupported()) return;

  const notification = new Notification({
    title: title || 'OET Prep',
    body: body || '',
    icon: path.join(__dirname, '..', 'public', 'icon.svg'),
    silent: false,
  });

  notification.on('click', () => {
    focusMainWindow();
    if (route) navigateToRoute(route);
  });

  notification.show();
}

// IPC handler for renderer to trigger native notifications (e.g., from SignalR)
ipcMain.handle('desktop:show-notification', async (event, { title, body, route }) => {
  if (!validateSenderFrame(event)) throw new Error('Unauthorized IPC sender.');
  showDesktopNotification(title, body, route);
  return { ok: true };
});

app.whenReady().then(async () => {
  try {
    app.setAppUserModelId('com.oetprep.desktop');
    applyApplicationSecurityPolicies();
    installCertificatePinning(session.defaultSession, { logger: console });

    session.defaultSession.on('will-download', (_downloadEvent, item) => {
      const suggestedFilename = item.getFilename();
      const savePath = dialog.showSaveDialogSync(mainWindow, {
        defaultPath: suggestedFilename,
      });

      if (!savePath) {
        item.cancel();
        return;
      }

      item.setSavePath(savePath);

      item.on('done', (_doneEvent, state) => {
        if (state === 'completed' && savePath) {
          shell.showItemInFolder(savePath);
        }
      });
    });

    secureSecretStore = createSecureSecretStore({ logger: console });

    if (!app.isPackaged) {
      console.info('[electron] desktop runtime paths', desktopDataPaths);
    }

    if (app.isPackaged) {
      app.setAsDefaultProtocolClient('oet-prep');
    }

    desktopUpdater = createDesktopUpdater({
      getWindow: () => mainWindow,
      onStateChange: () => refreshApplicationMenu(),
    });

    createSystemTray();

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
    showStartupFailure(error);
    app.quit();
  }
});

app.on('before-quit', () => {
  shuttingDown = true;
  stopUpdateCheckTimer();
  stopBackendServer();
  stopRendererServer();
});

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') {
    app.quit();
  }
});

ipcMain.handle('desktop:open-external', async (event, url) => {
  if (!validateSenderFrame(event)) {
    throw new Error('Unauthorized IPC sender.');
  }
  if (typeof url !== 'string' || url.trim() === '') {
    throw new Error('A valid URL is required.');
  }

  if (!isAllowedExternalUrl(url)) {
    throw new Error('Only http and https URLs can be opened externally.');
  }

  await shell.openExternal(url);
  return true;
});

ipcMain.handle('desktop:secret-storage:status', async (event) => {
  if (!validateSenderFrame(event)) {
    throw new Error('Unauthorized IPC sender.');
  }
  if (!secureSecretStore) {
    secureSecretStore = createSecureSecretStore({ logger: console });
  }

  return secureSecretStore.getStatus();
});

ipcMain.handle('desktop:secret-storage:get', async (event, payload) => {
  if (!validateSenderFrame(event)) {
    throw new Error('Unauthorized IPC sender.');
  }
  if (!secureSecretStore) {
    secureSecretStore = createSecureSecretStore({ logger: console });
  }

  const namespace = normalizeSecretNamespace(payload?.namespace || 'default');
  const key = normalizeSecretKey(payload?.key);
  return secureSecretStore.getSecret({ namespace, key });
});

ipcMain.handle('desktop:secret-storage:set', async (event, payload) => {
  if (!validateSenderFrame(event)) {
    throw new Error('Unauthorized IPC sender.');
  }
  if (!secureSecretStore) {
    secureSecretStore = createSecureSecretStore({ logger: console });
  }

  const namespace = normalizeSecretNamespace(payload?.namespace || 'default');
  const key = normalizeSecretKey(payload?.key);
  const value = normalizeSecretValue(payload?.value);
  return secureSecretStore.setSecret({ namespace, key, value });
});

ipcMain.handle('desktop:secret-storage:delete', async (event, payload) => {
  if (!validateSenderFrame(event)) {
    throw new Error('Unauthorized IPC sender.');
  }
  if (!secureSecretStore) {
    secureSecretStore = createSecureSecretStore({ logger: console });
  }

  const namespace = normalizeSecretNamespace(payload?.namespace || 'default');
  const key = normalizeSecretKey(payload?.key);
  return secureSecretStore.deleteSecret({ namespace, key });
});

ipcMain.handle('desktop:runtime-info', async (event) => {
  if (!validateSenderFrame(event)) {
    throw new Error('Unauthorized IPC sender.');
  }
  return {
    isPackaged: app.isPackaged,
    activeBackendUrl,
    ignoredPackagedLoopbackApiTarget,
    windowState: getMainWindowState(),
  };
});

// ---------- M3: Desktop Offline Content Cache ----------
const OFFLINE_CACHE_DIR = path.join(app.getPath('userData'), 'offline-content');

function ensureOfflineCacheDir() {
  if (!fs.existsSync(OFFLINE_CACHE_DIR)) {
    fs.mkdirSync(OFFLINE_CACHE_DIR, { recursive: true });
  }
}

function sanitizeCacheKey(key) {
  if (typeof key !== 'string' || key.trim() === '') {
    throw new Error('A valid cache key is required.');
  }
  // Only allow alphanumeric, hyphens, underscores, and dots
  return key.replace(/[^a-zA-Z0-9._-]/g, '_');
}

ipcMain.handle('desktop:offline-cache:store', async (event, payload) => {
  if (!validateSenderFrame(event)) {
    throw new Error('Unauthorized IPC sender.');
  }
  ensureOfflineCacheDir();
  const key = sanitizeCacheKey(payload?.key);
  const data = payload?.data;
  if (data === undefined || data === null) {
    throw new Error('Data is required for cache storage.');
  }
  const filePath = path.join(OFFLINE_CACHE_DIR, `${key}.json`);
  const resolved = path.resolve(filePath);
  if (!resolved.startsWith(OFFLINE_CACHE_DIR)) {
    throw new Error('Invalid cache key.');
  }
  fs.writeFileSync(resolved, JSON.stringify({ cachedAt: Date.now(), data }), 'utf-8');
  return { success: true, key };
});

ipcMain.handle('desktop:offline-cache:get', async (event, payload) => {
  if (!validateSenderFrame(event)) {
    throw new Error('Unauthorized IPC sender.');
  }
  ensureOfflineCacheDir();
  const key = sanitizeCacheKey(payload?.key);
  const filePath = path.join(OFFLINE_CACHE_DIR, `${key}.json`);
  const resolved = path.resolve(filePath);
  if (!resolved.startsWith(OFFLINE_CACHE_DIR)) {
    throw new Error('Invalid cache key.');
  }
  if (!fs.existsSync(resolved)) {
    return null;
  }
  const raw = fs.readFileSync(resolved, 'utf-8');
  return JSON.parse(raw);
});

ipcMain.handle('desktop:offline-cache:delete', async (event, payload) => {
  if (!validateSenderFrame(event)) {
    throw new Error('Unauthorized IPC sender.');
  }
  ensureOfflineCacheDir();
  const key = sanitizeCacheKey(payload?.key);
  const filePath = path.join(OFFLINE_CACHE_DIR, `${key}.json`);
  const resolved = path.resolve(filePath);
  if (!resolved.startsWith(OFFLINE_CACHE_DIR)) {
    throw new Error('Invalid cache key.');
  }
  if (fs.existsSync(resolved)) {
    fs.unlinkSync(resolved);
  }
  return { success: true, key };
});

ipcMain.handle('desktop:offline-cache:list', async (event) => {
  if (!validateSenderFrame(event)) {
    throw new Error('Unauthorized IPC sender.');
  }
  ensureOfflineCacheDir();
  const files = fs.readdirSync(OFFLINE_CACHE_DIR).filter((f) => f.endsWith('.json'));
  return files.map((f) => {
    const key = f.replace(/\.json$/, '');
    const stat = fs.statSync(path.join(OFFLINE_CACHE_DIR, f));
    return { key, sizeBytes: stat.size, modifiedAt: stat.mtimeMs };
  });
});

ipcMain.handle('desktop:offline-cache:clear', async (event) => {
  if (!validateSenderFrame(event)) {
    throw new Error('Unauthorized IPC sender.');
  }
  ensureOfflineCacheDir();
  const files = fs.readdirSync(OFFLINE_CACHE_DIR).filter((f) => f.endsWith('.json'));
  for (const f of files) {
    fs.unlinkSync(path.join(OFFLINE_CACHE_DIR, f));
  }
  return { success: true, cleared: files.length };
});

ipcMain.handle('desktop:get-dropped-file-info', async (event, filePath) => {
  if (!validateSenderFrame(event)) throw new Error('Unauthorized IPC sender.');
  try {
    const resolved = path.resolve(filePath);
    const stat = await fs.promises.stat(resolved);
    if (!stat.isFile()) return { ok: false, error: 'NOT_A_FILE' };
    return {
      ok: true,
      name: path.basename(resolved),
      size: stat.size,
      path: resolved,
      lastModified: stat.mtimeMs,
    };
  } catch {
    return { ok: false, error: 'FILE_NOT_FOUND' };
  }
});

ipcMain.handle('desktop:print-page', async (event, options) => {
  if (!validateSenderFrame(event)) throw new Error('Unauthorized IPC sender.');
  const win = BrowserWindow.getFocusedWindow();
  if (!win) return { ok: false, error: 'NO_WINDOW' };
  try {
    const printOptions = {
      silent: false,
      printBackground: true,
      margins: { marginType: 'default' },
      ...(options || {}),
    };
    await win.webContents.print(printOptions);
    return { ok: true };
  } catch (err) {
    return { ok: false, error: err.message || 'PRINT_FAILED' };
  }
});
