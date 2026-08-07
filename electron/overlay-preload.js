const { contextBridge, ipcRenderer } = require("electron");

contextBridge.exposeInMainWorld("fireOverlay", {
  onState: (callback) => {
    ipcRenderer.on("overlay:state", (_, payload) => callback(payload));
  },
});
