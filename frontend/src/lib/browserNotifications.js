const SW_PATH = "/sw.js";
const FIREBASE_APP_URL = "https://www.gstatic.com/firebasejs/10.12.5/firebase-app-compat.js";
const FIREBASE_MSG_URL = "https://www.gstatic.com/firebasejs/10.12.5/firebase-messaging-compat.js";

let runtimePushConfig = {
  vapidPublicKey: "",
  firebaseConfig: null,
};

export function setRuntimePushConfig(next = {}) {
  const firebaseConfig = next.firebaseConfig && typeof next.firebaseConfig === "object"
    ? next.firebaseConfig
    : null;
  runtimePushConfig = {
    vapidPublicKey: String(next.vapidPublicKey || "").trim(),
    firebaseConfig,
  };
}

export function getRuntimePushConfig() {
  return runtimePushConfig;
}

export async function ensureServiceWorkerRegistered() {
  if (typeof window === "undefined" || !("serviceWorker" in navigator)) return null;
  try {
    return await navigator.serviceWorker.register(SW_PATH);
  } catch {
    return null;
  }
}

export async function requestDesktopNotificationPermission() {
  if (typeof Notification === "undefined") return "unsupported";
  if (Notification.permission === "granted") return "granted";
  if (Notification.permission === "denied") return "denied";
  try {
    return await Notification.requestPermission();
  } catch {
    return "denied";
  }
}

export async function showDesktopNotification({ title, body, tag, data } = {}) {
  if (typeof document !== "undefined" && !document.hidden) return false;
  if (typeof Notification === "undefined" || Notification.permission !== "granted") return false;

  if ("serviceWorker" in navigator) {
    const reg = await ensureServiceWorkerRegistered();
    if (reg?.active) {
      reg.active.postMessage({
        type: "SHOW_NOTIFICATION",
        title,
        body,
        tag,
        data
      });
      return true;
    }
  }

  try {
    new Notification(title || "New message", {
      body: body || "You received a new message",
      tag: tag || "textzy-inbox",
      data: data || {}
    });
    return true;
  } catch {
    return false;
  }
}

function urlBase64ToUint8Array(base64String) {
  const padding = "=".repeat((4 - (base64String.length % 4)) % 4);
  const base64 = (base64String + padding).replace(/-/g, "+").replace(/_/g, "/");
  const rawData = window.atob(base64);
  const outputArray = new Uint8Array(rawData.length);
  for (let i = 0; i < rawData.length; ++i) outputArray[i] = rawData.charCodeAt(i);
  return outputArray;
}

export async function subscribePush(vapidPublicKey) {
  if (!vapidPublicKey) return null;
  if (typeof window === "undefined" || !("serviceWorker" in navigator) || !("PushManager" in window)) return null;
  const reg = await ensureServiceWorkerRegistered();
  if (!reg) return null;
  let sub = await reg.pushManager.getSubscription();
  if (!sub) {
    sub = await reg.pushManager.subscribe({
      userVisibleOnly: true,
      applicationServerKey: urlBase64ToUint8Array(vapidPublicKey)
    });
  }
  return sub ? sub.toJSON() : null;
}

function getFirebaseConfig() {
  const runtimeCfg = runtimePushConfig.firebaseConfig;
  if (runtimeCfg?.apiKey && runtimeCfg?.projectId && runtimeCfg?.messagingSenderId && runtimeCfg?.appId) {
    return {
      apiKey: String(runtimeCfg.apiKey || ""),
      authDomain: String(runtimeCfg.authDomain || ""),
      projectId: String(runtimeCfg.projectId || ""),
      storageBucket: String(runtimeCfg.storageBucket || ""),
      messagingSenderId: String(runtimeCfg.messagingSenderId || ""),
      appId: String(runtimeCfg.appId || ""),
      measurementId: String(runtimeCfg.measurementId || ""),
    };
  }

  const apiKey = process.env.REACT_APP_FIREBASE_API_KEY || "";
  const authDomain = process.env.REACT_APP_FIREBASE_AUTH_DOMAIN || "";
  const projectId = process.env.REACT_APP_FIREBASE_PROJECT_ID || "";
  const storageBucket = process.env.REACT_APP_FIREBASE_STORAGE_BUCKET || "";
  const messagingSenderId = process.env.REACT_APP_FIREBASE_MESSAGING_SENDER_ID || "";
  const appId = process.env.REACT_APP_FIREBASE_APP_ID || "";
  if (!apiKey || !projectId || !messagingSenderId || !appId) return null;
  return { apiKey, authDomain, projectId, storageBucket, messagingSenderId, appId };
}

async function loadScriptOnce(src, attr) {
  if (document.querySelector(`script[${attr}='1']`)) return true;
  await new Promise((resolve, reject) => {
    const s = document.createElement("script");
    s.src = src;
    s.async = true;
    s.setAttribute(attr, "1");
    s.onload = () => resolve(true);
    s.onerror = () => reject(new Error(`Failed to load ${src}`));
    document.head.appendChild(s);
  });
  return true;
}

export async function subscribeFcm(vapidPublicKey, firebaseConfigOverride = null) {
  if (firebaseConfigOverride && typeof firebaseConfigOverride === "object") {
    setRuntimePushConfig({
      vapidPublicKey: runtimePushConfig.vapidPublicKey,
      firebaseConfig: firebaseConfigOverride,
    });
  }
  const cfg = getFirebaseConfig();
  if (!cfg || !vapidPublicKey) return null;
  if (typeof window === "undefined" || !("serviceWorker" in navigator)) return null;
  try {
    await loadScriptOnce(FIREBASE_APP_URL, "data-firebase-app");
    await loadScriptOnce(FIREBASE_MSG_URL, "data-firebase-msg");
    if (!window.firebase) return null;
    let app;
    if (window.firebase.apps?.length) app = window.firebase.app();
    else app = window.firebase.initializeApp(cfg);
    const messaging = window.firebase.messaging(app);
    const reg = await ensureServiceWorkerRegistered();
    const token = await messaging.getToken({
      vapidKey: vapidPublicKey,
      serviceWorkerRegistration: reg || undefined
    });
    return token || null;
  } catch {
    return null;
  }
}
