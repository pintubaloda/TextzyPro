// Extracted mobile shell core constants and helpers

export const C = {
  /* brand */
  orange:       "#F97316",
  orangeDark:   "#EA6C0A",
  orangeLight:  "#FB923C",
  orangePale:   "#FFF7ED",
  orangeLight2: "#FFEDD5",

  /* replacing all blacks with warm deep teal */
  headerBg:     "#1E3A5F",   /* deep navy-blue (NOT black) */
  headerText:   "#FFFFFF",
  headerSub:    "rgba(255,255,255,0.65)",

  /* surfaces */
  bg:           "#F8FAFC",
  sidebarBg:    "#FFFFFF",
  chatBg:       "#F1F5F9",
  bubbleSent:   "#FFEDD5",
  bubbleRecv:   "#FFFFFF",
  inputBg:      "#FFFFFF",
  panelBg:      "#F8FAFC",

  /* text — warm slates, never black */
  textMain:     "#1E3A5F",
  textSub:      "#64748B",
  textMuted:    "#94A3B8",
  textLight:    "#FFFFFF",

  /* ui */
  divider:      "#E2E8F0",
  hover:        "#F8FAFC",
  selected:     "#FFF7ED",
  unread:       "#F97316",
  online:       "#22C55E",
  iconColor:    "#64748B",
  danger:       "#EF4444",
  scanLine:     "#F97316",
};

export const API_BASE =
  (typeof window !== "undefined" && window.__APP_CONFIG__?.API_BASE) ||
  process.env.REACT_APP_API_BASE ||
  process.env.VITE_API_BASE ||
  "https://textzy-backend-production.up.railway.app";

export const SESSION_KEY = "textzy.mobile.session";
export const DEVICE_KEY = "textzy.mobile.device";

export const idempotencyKey = () =>
  `mobile-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`;

export const readCookie = (name) => {
  if (typeof document === "undefined") return "";
  const key = `${name}=`;
  const parts = document.cookie.split(";").map((x) => x.trim());
  const row = parts.find((x) => x.startsWith(key));
  return row ? decodeURIComponent(row.slice(key.length)) : "";
};

export const resolveCsrf = (token) => token || readCookie("textzy_csrf") || "";

export const parsePairingToken = (raw) => {
  const input = String(raw || "").trim();
  if (!input) return "";

  const fromObject = (obj) =>
    obj?.pairingToken ||
    obj?.pairing_token ||
    obj?.pairToken ||
    obj?.token ||
    obj?.Token ||
    obj?.payload?.pairingToken ||
    obj?.payload?.pairing_token ||
    obj?.payload?.token ||
    obj?.payload?.Token ||
    "";

  const tryJson = (text) => {
    try {
      const obj = JSON.parse(text);
      return fromObject(obj) || "";
    } catch {
      return "";
    }
  };

  // Plain JSON
  let token = tryJson(input);
  if (token) return String(token).trim();

  // URL-encoded JSON (common with QR image providers)
  const decodedOnce = (() => {
    try {
      return decodeURIComponent(input);
    } catch {
      return input;
    }
  })();
  token = tryJson(decodedOnce);
  if (token) return String(token).trim();

  // Query string token
  const tokenFromUrl =
    input.match(/[?&](pairingToken|pairing_token|pairToken|token)=([^&]+)/i) ||
    decodedOnce.match(/[?&](pairingToken|pairing_token|pairToken|token)=([^&]+)/i);
  if (tokenFromUrl?.[2]) return decodeURIComponent(tokenFromUrl[2]).trim();

  // Regex extraction from JSON-like text
  const tokenRegex =
    /"(pairingToken|pairing_token|pairToken|token|Token)"\s*:\s*"([^"]+)"/i;
  const directMatch = input.match(tokenRegex) || decodedOnce.match(tokenRegex);
  if (directMatch?.[2]) return directMatch[2].trim();

  return "";
};

export const parseJsonSafe = (raw, fallback = {}) => {
  try {
    return JSON.parse(raw);
  } catch {
    return fallback;
  }
};

export const getNativeDeviceInfo = () => {
  if (typeof window === "undefined") return {};
  try {
    const raw = window.TextzyNative?.getDeviceInfo?.();
    if (!raw) return {};
    const parsed = parseJsonSafe(String(raw), {});
    return parsed && typeof parsed === "object" ? parsed : {};
  } catch {
    return {};
  }
};

export const resolveDeviceContext = (restored = {}) => {
  const stored =
    typeof localStorage !== "undefined"
      ? parseJsonSafe(localStorage.getItem(DEVICE_KEY) || "{}", {})
      : {};
  const native = getNativeDeviceInfo();
  const fallbackInstallId =
    restored.installId ||
    stored.installId ||
    `web-mobile-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
  const ctx = {
    installId: native.installId || fallbackInstallId,
    deviceName: native.deviceName || stored.deviceName || "Textzy Mobile",
    devicePlatform: native.devicePlatform || stored.devicePlatform || "android",
    deviceModel: native.deviceModel || stored.deviceModel || "webview",
    osVersion: native.osVersion || stored.osVersion || "android",
    appVersion: native.appVersion || stored.appVersion || "1.0.0",
  };
  if (typeof window !== "undefined") window.__TEXTZY_MOBILE_DEVICE__ = ctx;
  if (typeof localStorage !== "undefined") {
    localStorage.setItem(DEVICE_KEY, JSON.stringify(ctx));
  }
  return ctx;
};

export const getDeviceLocation = async () => {
  if (typeof navigator === "undefined" || !navigator.geolocation?.getCurrentPosition) return null;
  return new Promise((resolve) => {
    let settled = false;
    const finish = (value) => {
      if (settled) return;
      settled = true;
      resolve(value);
    };
    const timer = setTimeout(() => finish(null), 5000);
    navigator.geolocation.getCurrentPosition(
      (pos) => {
        clearTimeout(timer);
        finish({
          latitude: pos.coords.latitude,
          longitude: pos.coords.longitude,
          accuracyMeters: pos.coords.accuracy,
          capturedAtUtc: new Date().toISOString(),
        });
      },
      () => {
        clearTimeout(timer);
        finish(null);
      },
      {
        enableHighAccuracy: false,
        timeout: 4500,
        maximumAge: 5 * 60 * 1000,
      }
    );
  });
};

export async function apiFetch(path, { method = "GET", tenantSlug = "", csrfToken = "", body, extraHeaders = {} } = {}) {
  const headers = { ...extraHeaders };
  if (typeof window !== "undefined" && window.__TEXTZY_MOBILE_DEVICE__) {
    const device = window.__TEXTZY_MOBILE_DEVICE__;
    if (device.installId) headers["X-Install-Id"] = String(device.installId);
    if (device.devicePlatform) headers["X-Device-Platform"] = String(device.devicePlatform);
    if (device.deviceModel) headers["X-Device-Model"] = String(device.deviceModel);
    if (device.appVersion) headers["X-App-Version"] = String(device.appVersion);
  }
  if (tenantSlug && !path.startsWith("/api/auth/") && !path.startsWith("/api/public/")) headers["X-Tenant-Slug"] = tenantSlug;
  if (path === "/api/messages/send" && !headers["Idempotency-Key"]) {
    headers["Idempotency-Key"] = body?.idempotencyKey || idempotencyKey();
  }
  const isFormData = typeof FormData !== "undefined" && body instanceof FormData;
  if (body != null && !isFormData) headers["Content-Type"] = "application/json";
  if (["POST", "PUT", "PATCH", "DELETE"].includes(method.toUpperCase())) {
    const csrf = resolveCsrf(csrfToken);
    if (csrf) headers["X-CSRF-Token"] = csrf;
  }

  const res = await fetch(`${API_BASE}${path}`, {
    method,
    headers,
    body: body == null ? undefined : (isFormData ? body : JSON.stringify(body)),
    credentials: "include",
    cache: "no-store",
  });

  const nextTokenHeader = res.headers.get("x-access-token") || "";
  const nextCsrfHeader = res.headers.get("x-csrf-token") || "";
  return { res, nextTokenHeader, nextCsrfHeader };
}

