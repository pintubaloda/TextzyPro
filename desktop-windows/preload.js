const { contextBridge, ipcRenderer } = require("electron");

contextBridge.exposeInMainWorld("TextzyDesktop", {
  platform: "windows",
  version: "1.0.0",
  refresh: () => ipcRenderer.invoke("textzy:refresh"),
  logout: () => ipcRenderer.invoke("textzy:logout")
});
