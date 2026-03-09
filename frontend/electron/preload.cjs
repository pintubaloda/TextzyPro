const { contextBridge } = require("electron");

contextBridge.exposeInMainWorld("textzyDesktop", {
  platform: "windows",
  app: "Textzy Desktop",
});
