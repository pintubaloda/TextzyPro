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
    // Prefer hosted app in production so cookies/CORS behave like the website.
    // Important: DO NOT fall back to file:// app build for auth flows.
    // When the UI loads under file://, browser cookies become "cross-site" and SameSite=Lax cookies
    // will not be sent on XHR/fetch/SignalR negotiate, leading to 401 "Missing bearer token"/"Invalid session".
    // Instead, show a simple offline screen and let the user retry.
    const remoteUrl = process.env.TEXTZY_DESKTOP_REMOTE_URL || "https://textzy.in/?desktopShell=1";
    let showedOffline = false;

    const showOffline = (reason = "") => {
      if (showedOffline) return;
      showedOffline = true;
      logger.log("Showing offline screen. reason=", reason);
      const html = `<!doctype html>
<html lang="en">
  <head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>Textzy Desktop</title>
    <style>
      :root { color-scheme: light; }
      body {
        margin: 0;
        font-family: system-ui, -apple-system, Segoe UI, Roboto, Arial, sans-serif;
        background: linear-gradient(180deg, #fff7ed 0%, #ffffff 40%, #f8fafc 100%);
        color: #0f172a;
      }
      .wrap {
        min-height: 100vh;
        display: grid;
        place-items: center;
        padding: 24px;
      }
      .card {
        width: min(720px, 100%);
        background: #ffffff;
        border: 1px solid #e2e8f0;
        border-radius: 16px;
        box-shadow: 0 20px 50px rgba(15, 23, 42, 0.08);
        padding: 22px 22px 18px 22px;
      }
      h1 {
        margin: 0 0 6px 0;
        font-size: 22px;
        letter-spacing: -0.01em;
      }
      p { margin: 0 0 10px 0; line-height: 1.5; color: #334155; }
      .pill {
        display: inline-block;
        font-size: 12px;
        background: #fff7ed;
        border: 1px solid #fed7aa;
        color: #9a3412;
        padding: 6px 10px;
        border-radius: 999px;
        margin-top: 6px;
      }
      .hint {
        margin-top: 14px;
        font-size: 13px;
        color: #475569;
        background: #f8fafc;
        border: 1px solid #e2e8f0;
        border-radius: 12px;
        padding: 12px;
      }
      code { font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; font-size: 12px; }
      .reason {
        margin-top: 10px;
        font-size: 12px;
        color: #64748b;
        white-space: pre-wrap;
        word-break: break-word;
      }
    </style>
  </head>
  <body>
    <div class="wrap">
      <div class="card">
        <h1>Cannot load Textzy</h1>
        <p>Textzy Desktop needs access to <code>https://textzy.in</code>.</p>
        <div class="pill">Retry: View -> Reload</div>
        <div class="hint">
          If this keeps happening, check:
          <br/>1) Internet/DNS on this machine
          <br/>2) Firewall/proxy blocking <code>textzy.in</code>
          <br/>3) Backend status at <code>https://api.textzy.in/api/public/status</code>
          <br/><br/>Log file:
          <br/><code>${logger.file}</code>
        </div>
        ${reason ? `<div class="reason">Details: ${String(reason).replace(/</g, "&lt;")}</div>` : ""}
      </div>
    </div>
  </body>
</html>`;
      const url = `data:text/html;charset=utf-8,${encodeURIComponent(html)}`;
      win.loadURL(url).catch((e) => logger.log("Offline loadURL failed:", e?.message || String(e)));
    };

    logger.log("Loading remote:", remoteUrl);
    win.loadURL(remoteUrl).catch((e) => {
      logger.log("Remote loadURL failed:", e?.message || String(e));
      showOffline(e?.message || String(e));
    });

    // If remote cannot load quickly, show offline screen (better than falling back to file://).
    const failTimer = setTimeout(() => showOffline("Timed out loading hosted UI."), 15000);
    win.webContents.once("did-finish-load", () => {
      clearTimeout(failTimer);
    });
    win.webContents.once("did-fail-load", (_evt, code, desc, validatedUrl) => {
      clearTimeout(failTimer);
      showOffline(`did-fail-load code=${code} desc=${desc} url=${validatedUrl}`);
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
