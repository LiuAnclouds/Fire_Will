const { contextBridge, ipcRenderer } = require("electron");

contextBridge.exposeInMainWorld("fireWill", {
  getState: () => ipcRenderer.invoke("project:get-state"),
  saveBindings: (payload) => ipcRenderer.invoke("project:save-bindings", payload),
  getAssets: () => ipcRenderer.invoke("project:get-assets"),
  launchBackend: () => ipcRenderer.invoke("backend:launch"),
  setZoom: (action) => ipcRenderer.invoke("window:set-zoom", action),
  onZoomChanged: (callback) => {
    ipcRenderer.on("window:zoom-changed", (_, percent) => callback(percent));
  },
});
