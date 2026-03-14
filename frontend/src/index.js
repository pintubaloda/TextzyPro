import React, { Suspense, lazy } from "react";
import ReactDOM from "react-dom/client";
import "@/index.css";
import App from "@/App";
import { ensureServiceWorkerRegistered } from "@/lib/browserNotifications";
import { getRuntimeFlags } from "@/lib/runtimeFlags";

const TextzyMobile = lazy(() => import("@/textzy-mobile"));

const { desktopShell, mobileShell } = getRuntimeFlags();
try {
  window.__TEXTZY_DESKTOP_SHELL__ = !!desktopShell;
  window.__TEXTZY_MOBILE_SHELL__ = !!mobileShell;
} catch {
  // ignore
}

const root = ReactDOM.createRoot(document.getElementById("root"));
root.render(
  <React.StrictMode>
    {mobileShell || desktopShell ? (
      <Suspense fallback={<div style={{ padding: 16, fontFamily: "system-ui, sans-serif" }}>Loading…</div>}>
        <TextzyMobile />
      </Suspense>
    ) : (
      <App />
    )}
  </React.StrictMode>,
);

ensureServiceWorkerRegistered().catch(() => {});
