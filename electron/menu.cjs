const { app, Menu, shell } = require('electron');

function createNavigateMenu(onNavigate) {
  return {
    label: 'Navigate',
    submenu: [
      { label: 'Home', accelerator: 'CmdOrCtrl+Shift+1', click: () => onNavigate('/') },
      { label: 'Dashboard', accelerator: 'CmdOrCtrl+Shift+2', click: () => onNavigate('/dashboard') },
      { label: 'Study Plan', accelerator: 'CmdOrCtrl+Shift+3', click: () => onNavigate('/study-plan') },
      { label: 'Readiness', accelerator: 'CmdOrCtrl+Shift+4', click: () => onNavigate('/readiness') },
      { label: 'Goals', accelerator: 'CmdOrCtrl+Shift+5', click: () => onNavigate('/goals') },
      { type: 'separator' },
      { label: 'Listening', click: () => onNavigate('/listening') },
      { label: 'Reading', click: () => onNavigate('/reading') },
      { label: 'Writing', click: () => onNavigate('/writing') },
      { label: 'Speaking', click: () => onNavigate('/speaking') },
      { type: 'separator' },
      { label: 'Expert', click: () => onNavigate('/expert') },
      { label: 'Admin', click: () => onNavigate('/admin') },
      { type: 'separator' },
      { label: 'Settings', accelerator: 'CmdOrCtrl+,', click: () => onNavigate('/settings') },
    ],
  };
}

function createHelpMenu({ updateState, onCheckForUpdates, onInstallUpdate, onShowAbout }) {
  return {
    label: 'Help',
    submenu: [
      {
        label: 'Check for Updates',
        enabled: updateState.enabled && !updateState.downloading,
        click: () => onCheckForUpdates(),
      },
      {
        label: 'Install Update and Restart',
        visible: updateState.downloaded,
        enabled: updateState.downloaded,
        click: () => onInstallUpdate(),
      },
      { type: 'separator' },
      {
        label: 'About OET Prep',
        click: () => onShowAbout(),
      },
      { type: 'separator' },
      {
        label: 'Open Release Notes',
        click: () => shell.openExternal('https://www.electron.build/publish.html'),
      },
    ],
  };
}

function createDesktopMenu({
  isDev,
  updateState,
  onNavigate,
  onCheckForUpdates,
  onInstallUpdate,
  onShowAbout,
}) {
  const template = [];

  if (process.platform === 'darwin') {
    template.push({
      label: app.name,
      submenu: [
        { role: 'about' },
        { type: 'separator' },
        { role: 'hide' },
        { role: 'hideOthers' },
        { role: 'unhide' },
        { type: 'separator' },
        { role: 'quit' },
      ],
    });
  }

  template.push(createNavigateMenu(onNavigate));
  template.push({
    label: 'Edit',
    submenu: [
      { role: 'undo' },
      { role: 'redo' },
      { type: 'separator' },
      { role: 'cut' },
      { role: 'copy' },
      { role: 'paste' },
      { role: 'selectAll' },
    ],
  });
  template.push({
    label: 'View',
    submenu: [
      { role: 'reload' },
      { role: 'forceReload', visible: isDev },
      { role: 'toggleDevTools', visible: isDev },
      { type: 'separator' },
      { role: 'resetZoom' },
      { role: 'zoomIn' },
      { role: 'zoomOut' },
      { type: 'separator' },
      { role: 'togglefullscreen' },
    ],
  });
  template.push({
    label: 'Window',
    submenu: [
      { role: 'minimize' },
      { role: 'zoom' },
      { role: 'close' },
    ],
  });
  template.push(createHelpMenu({ updateState, onCheckForUpdates, onInstallUpdate, onShowAbout }));

  return Menu.buildFromTemplate(template);
}

module.exports = {
  createDesktopMenu,
};