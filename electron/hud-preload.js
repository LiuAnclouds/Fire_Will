const { contextBridge, ipcRenderer } = require("electron");

contextBridge.exposeInMainWorld("fireHud", {
  onState: (callback) => {
    ipcRenderer.on("hud:state", (_, payload) => callback(payload));
  },
});
