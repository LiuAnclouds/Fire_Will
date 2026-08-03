const { contextBridge, ipcRenderer } = require("electron");

contextBridge.exposeInMainWorld("fireWill", {
  getState: () => ipcRenderer.invoke("project:get-state"),
  saveLayout: (payload) => ipcRenderer.invoke("project:save-layout", payload),
  saveProfileAs: (profileName) => ipcRenderer.invoke("project:save-profile-as", profileName),
  loadProfile: (profileName) => ipcRenderer.invoke("project:load-profile", profileName),
  saveBindings: (payload) => ipcRenderer.invoke("project:save-bindings", payload),
  getAssets: () => ipcRenderer.invoke("project:get-assets"),
  launchBackend: () => ipcRenderer.invoke("backend:launch"),
  initializeGameSession: () => ipcRenderer.invoke("game:initialize"),
  getGameSession: () => ipcRenderer.invoke("game:get-session"),
  setZoom: (action) => ipcRenderer.invoke("window:set-zoom", action),
  onZoomChanged: (callback) => {
    ipcRenderer.on("window:zoom-changed", (_, percent) => callback(percent));
  },
});
