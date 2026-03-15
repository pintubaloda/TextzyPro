const { app, BrowserWindow, shell, Menu, dialog } = require("electron");
const path = require("path");
const fs = require("fs");
const os = require("os");

function ensureDir(p) {
  try { fs.mkdirSync(p, { recursive: true }); } catch {}
}

function createLogger() {
  try {
    const base = app.getPath("userData");
    const dir = path.join(base, "logs");
    ensureDir(dir);
    const file = path.join(dir, "desktop.log");
    return {
      file,
      log: (...args) => {
        const line = `[${new Date().toISOString()}] ${args.map((x) => String(x)).join(" ")}${os.EOL}`;
        try { fs.appendFileSync(file, line, "utf8"); } catch {}
      },
    };
  } catch {
    return { file: "", log: () => {} };
  }
}

const isDev = !app.isPackaged;

function createWindow() {
  const logger = createLogger();
  logger.log("Starting window. isDev=", isDev, "appPath=", app.getAppPath());

  const win = new BrowserWindow({
    width: 1420,
    height: 920,
    minWidth: 1100,
    minHeight: 720,
    title: "Textzy Desktop",
    backgroundColor: "#111827",
    webPreferences: {
      preload: path.join(__dirname, "preload.cjs"),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
    },
  });

  // Basic app menu with devtools + reload to help diagnose blank screen issues in production builds.
  try {
    const template = [
      {
        label: "View",
        submenu: [
          { role: "reload" },
          { role: "forceReload" },
          { role: "toggleDevTools" },
          { type: "separator" },
          { role: "resetZoom" },
          { role: "zoomIn" },
          { role: "zoomOut" },
          { type: "separator" },
          { role: "togglefullscreen" },
        ],
      },
    ];
    Menu.setApplicationMenu(Menu.buildFromTemplate(template));
  } catch {}

  win.webContents.on("did-fail-load", (_evt, code, desc, validatedUrl) => {
    logger.log("did-fail-load", "code=", code, "desc=", desc, "url=", validatedUrl);
    try {
      dialog.showErrorBox(
        "Textzy Desktop failed to load",
        `Unable to load the app UI (code ${code}).\n\n${desc}\n\nLog: ${logger.file}`
      );
    } catch {}
  });
  win.webContents.on("render-process-gone", (_evt, details) => {
    logger.log("render-process-gone", JSON.stringify(details || {}));
  });
  win.webContents.on("unresponsive", () => {
    logger.log("unresponsive");
  });
  win.webContents.on("console-message", (_evt, level, message, line, sourceId) => {
    logger.log("console", "level=", level, "line=", line, "source=", sourceId, "msg=", message);
  });

  if (isDev) {
    win.loadURL("http://localhost:3000/?desktopShell=1");
  } else {
    // Prefer hosted app in production so cookies/CORS behave like the website (fixes "0 projects" after restart).
    // Fall back to local bundled build if remote fails (offline / DNS / backend maintenance).
    const remoteUrl = process.env.TEXTZY_DESKTOP_REMOTE_URL || "https://textzy.in/?desktopShell=1";
    const indexPath = path.join(__dirname, "..", "build", "index.html");
    let fellBack = false;

    const loadLocal = () => {
      if (fellBack) return;
      fellBack = true;
      logger.log("Falling back to local file:", indexPath);
      win.loadFile(indexPath, { query: { desktopShell: "1" } }).catch((e) => {
        logger.log("Local loadFile failed:", e?.message || String(e));
      });
    };

    logger.log("Loading remote:", remoteUrl);
    win.loadURL(remoteUrl).catch((e) => {
      logger.log("Remote loadURL failed:", e?.message || String(e));
      loadLocal();
    });

    // If remote cannot load (e.g. during deploy) we should recover quickly.
    const failTimer = setTimeout(loadLocal, 9000);
    win.webContents.once("did-finish-load", () => {
      clearTimeout(failTimer);
    });
    win.webContents.once("did-fail-load", () => {
      clearTimeout(failTimer);
      loadLocal();
    });
  }

  if (!isDev && process.env.TEXTZY_DESKTOP_DEBUG === "1") {
    win.webContents.openDevTools({ mode: "detach" });
  }

  win.webContents.setWindowOpenHandler(({ url: target }) => {
    shell.openExternal(target);
    return { action: "deny" };
  });
}

app.whenReady().then(() => {
  createWindow();
  app.on("activate", () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow();
  });
});

app.on("window-all-closed", () => {
  if (process.platform !== "darwin") app.quit();
});
