import { getNotificationVolume, isNotificationSoundEnabled } from "@/lib/notificationAudio";

let cached = null;
let lastPlayAt = 0;

function isDesktopShellRuntime() {
  try {
    return window?.__TEXTZY_DESKTOP_SHELL__ === true;
  } catch {
    return false;
  }
}

function getSoundUrl(name) {
  const base = (process.env.PUBLIC_URL || "").replace(/\/$/, "");
  return `${base}/sounds/${name}`;
}

export function playDesktopSound(kind = "notify") {
  try {
    if (!isDesktopShellRuntime()) return false;
    if (!isNotificationSoundEnabled()) return false;

    const now = Date.now();
    if (now - lastPlayAt < 650) return false;
    lastPlayAt = now;

    const volumeSetting = getNotificationVolume(); // 0..2
    const volume = Math.max(0, Math.min(1, Number(volumeSetting) / 2));

    const src = kind === "inbound" ? "notify.wav" : "notify.wav";
    if (!cached || cached.src !== src) {
      const audio = new Audio(getSoundUrl(src));
      audio.preload = "auto";
      cached = { src, audio };
    }

    cached.audio.volume = volume;
    try {
      cached.audio.currentTime = 0;
    } catch {
      // ignore
    }
    cached.audio.play().catch(() => {});
    return true;
  } catch {
    return false;
  }
}

