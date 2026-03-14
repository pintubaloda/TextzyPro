function safeWindow() {
  try {
    return typeof window === "undefined" ? null : window;
  } catch {
    return null;
  }
}

export function getRuntimeFlags() {
  const w = safeWindow();
  if (!w) return { desktopShell: false, mobileShell: false };
  const params = new URLSearchParams(w.location.search || "");
  const desktopShell = params.get("desktopShell") === "1";
  const mobileShell =
    params.get("mobileShell") === "1" ||
    w.location.pathname.startsWith("/mobile-shell") ||
    String(w.location.href || "").includes("mobileShell=1") ||
    String(w.location.hash || "").toLowerCase().includes("mobileshell") ||
    String(w.location.hash || "").toLowerCase().includes("mobile-shell") ||
    String(w.navigator?.userAgent || "").includes("TextzyMobileShell/1");

  return { desktopShell, mobileShell };
}

