const { contextBridge } = require("electron");

contextBridge.exposeInMainWorld("TextzyDesktop", {
  platform: "windows",
  version: "1.0.0"
});
