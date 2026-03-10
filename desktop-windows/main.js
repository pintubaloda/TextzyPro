const { app, BrowserWindow, Menu, Tray, ipcMain, session } = require("electron");
const path = require("path");

const SHELL_URL = "https://textzy.in/?desktopShell=1&mobileShell=1&platform=windows";
const LOGIN_URL = "https://textzy.in/login?desktopShell=1&mobileShell=1&platform=windows";
const TRUSTED_HOSTS = new Set([
  "textzy.in",
  "www.textzy.in",
  "api.textzy.in"
]);
const PERSIST_PARTITION = "persist:textzy_session";
const APP_ID = "com.textzy.windows.desktop";

let mainWindow = null;
let tray = null;
let isQuitting = false;

function normalizeHost(url) {
  try {
    return new URL(url).host.toLowerCase();
  } catch {
    return "";
  }
}

function createTray() {
  if (tray) return;
  tray = new Tray(path.join(__dirname, "assets", "icon.png"));
  const contextMenu = Menu.buildFromTemplate([
    { label: "Open Textzy", click: () => mainWindow?.show() },
    { label: "Refresh", click: () => mainWindow?.reload() },
    { label: "Logout", click: () => ipcMain.emit("textzy:logout") },
    { type: "separator" },
    { label: "Quit", click: () => { isQuitting = true; app.quit(); } }
  ]);
  tray.setToolTip("Textzy Desktop");
  tray.setContextMenu(contextMenu);
  tray.on("double-click", () => mainWindow?.show());
}

function injectDesktopControls(win) {
  win.webContents.executeJavaScript(`
    (function() {
      if (document.getElementById("textzy-desktop-controls")) return;
      const root = document.createElement("div");
      root.id = "textzy-desktop-controls";
      root.style.position = "fixed";
      root.style.top = "16px";
      root.style.right = "16px";
      root.style.zIndex = "2147483647";
      root.style.display = "flex";
      root.style.gap = "8px";
      const mkBtn = (label) => {
        const btn = document.createElement("button");
        btn.textContent = label;
        btn.style.background = "#ff7a1a";
        btn.style.color = "#fff";
        btn.style.border = "none";
        btn.style.padding = "8px 12px";
        btn.style.borderRadius = "999px";
        btn.style.cursor = "pointer";
        btn.style.fontSize = "12px";
        btn.style.boxShadow = "0 6px 20px rgba(0,0,0,0.18)";
        return btn;
      };
      const refresh = mkBtn("Refresh");
      refresh.onclick = () => window.TextzyDesktop?.refresh?.();
      const logout = mkBtn("Logout");
      logout.onclick = () => window.TextzyDesktop?.logout?.();
      root.appendChild(refresh);
      root.appendChild(logout);
      document.body.appendChild(root);
    })();
  `).catch(() => {});
}

function createWindow() {
  const win = new BrowserWindow({
    width: 1366,
    height: 860,
    minWidth: 1100,
    minHeight: 700,
    title: "Textzy",
    webPreferences: {
      preload: path.join(__dirname, "preload.js"),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
      backgroundThrottling: false,
      partition: PERSIST_PARTITION
    },
    icon: path.join(__dirname, "assets", "icon.png")
  });

  mainWindow = win;
  win.removeMenu();
  win.webContents.setUserAgent(`${win.webContents.getUserAgent()} TextzyDesktopShell/1 TextzyWindowsShell/1`);
  win.webContents.loadURL(SHELL_URL);
  win.webContents.on("did-finish-load", () => injectDesktopControls(win));

  win.on("close", (event) => {
    if (!isQuitting) {
      event.preventDefault();
      win.hide();
    }
  });

  return win;
}

app.whenReady().then(() => {
  app.setAppUserModelId(APP_ID);
  app.setLoginItemSettings({
    openAtLogin: true,
    path: app.getPath("exe"),
    args: ["--hidden"]
  });

  const scopedSession = session.fromPartition(PERSIST_PARTITION);
  scopedSession.webRequest.onBeforeRequest((details, callback) => {
    try {
      const host = normalizeHost(details.url);
      if (
        !TRUSTED_HOSTS.has(host) &&
        !host.endsWith(".gstatic.com") &&
        !host.endsWith(".googleapis.com")
      ) {
        callback({ cancel: true });
        return;
      }
    } catch {
      callback({ cancel: true });
      return;
    }
    callback({ cancel: false });
  });

  const win = createWindow();
  createTray();

  if (process.argv.includes("--hidden")) {
    win.hide();
  }

  app.on("activate", () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow();
  });
});

app.on("window-all-closed", () => {
  if (process.platform !== "darwin") app.quit();
});

ipcMain.handle("textzy:refresh", () => {
  mainWindow?.reload();
});

ipcMain.handle("textzy:logout", async () => {
  if (!mainWindow) return;
  const scopedSession = session.fromPartition(PERSIST_PARTITION);
  await scopedSession.clearStorageData();
  await mainWindow.loadURL(LOGIN_URL);
});
