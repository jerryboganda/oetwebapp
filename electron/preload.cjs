const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('desktopBridge', {
  platform: process.platform,
  versions: {
    electron: process.versions.electron,
    chrome: process.versions.chrome,
    node: process.versions.node,
  },
  openExternal: (url) => ipcRenderer.invoke('desktop:open-external', url),
  runtime: {
    info: () => ipcRenderer.invoke('desktop:runtime-info'),
    onWindowStateChange: (listener) => {
      const handler = (_event, windowState) => {
        listener(windowState);
      };

      ipcRenderer.on('desktop:window-state-changed', handler);

      return () => {
        ipcRenderer.removeListener('desktop:window-state-changed', handler);
      };
    },
  },
  secureSecrets: {
    get: (namespace, key) => ipcRenderer.invoke('desktop:secret-storage:get', { namespace, key }),
    set: (namespace, key, value) => ipcRenderer.invoke('desktop:secret-storage:set', { namespace, key, value }),
    delete: (namespace, key) => ipcRenderer.invoke('desktop:secret-storage:delete', { namespace, key }),
    status: () => ipcRenderer.invoke('desktop:secret-storage:status'),
  },
});
