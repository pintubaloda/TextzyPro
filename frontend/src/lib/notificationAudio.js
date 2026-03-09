const ENABLE_KEY = "textzy.notifications.enabled";
const SESSION_UNLOCK_KEY = "textzy.notifications.unlocked";
const EVER_ENABLED_KEY = "textzy.notifications.everEnabled";
const VOLUME_KEY = "textzy.notifications.volume";

let sharedAudioContext = null;

function getCtxClass() {
  if (typeof window === "undefined") return null;
  return window.AudioContext || window.webkitAudioContext || null;
}

export function isNotificationSoundEnabled() {
  try {
    return localStorage.getItem(ENABLE_KEY) !== "off";
  } catch {
    return true;
  }
}

export function setNotificationSoundEnabled(enabled) {
  try {
    localStorage.setItem(ENABLE_KEY, enabled ? "on" : "off");
    if (enabled) localStorage.setItem(EVER_ENABLED_KEY, "1");
  } catch {
    // ignore
  }
}

export function getNotificationVolume() {
  try {
    const raw = Number(localStorage.getItem(VOLUME_KEY) || 1);
    if (!Number.isFinite(raw)) return 1;
    return Math.max(0, Math.min(2, raw));
  } catch {
    return 1;
  }
}

export function setNotificationVolume(volume) {
  try {
    const normalized = Math.max(0, Math.min(2, Number(volume) || 1));
    localStorage.setItem(VOLUME_KEY, String(normalized));
  } catch {
    // ignore
  }
}

export function wasNotificationEverEnabled() {
  try {
    return localStorage.getItem(EVER_ENABLED_KEY) === "1";
  } catch {
    return false;
  }
}

export function isNotificationAudioUnlocked() {
  try {
    return sessionStorage.getItem(SESSION_UNLOCK_KEY) === "1";
  } catch {
    return false;
  }
}

export async function unlockNotificationAudio() {
  const Ctx = getCtxClass();
  if (!Ctx) return false;
  try {
    if (!sharedAudioContext) sharedAudioContext = new Ctx();
    await sharedAudioContext.resume();
    const ok = sharedAudioContext.state === "running";
    if (ok) {
      setNotificationSoundEnabled(true);
      try {
        sessionStorage.setItem(SESSION_UNLOCK_KEY, "1");
      } catch {
        // ignore
      }
    }
    return ok;
  } catch {
    return false;
  }
}

export function playNotificationTone(style = "classic", frequency = 880) {
  try {
    if (!isNotificationSoundEnabled()) return false;
    if (!isNotificationAudioUnlocked()) return false;
    if (!sharedAudioContext || sharedAudioContext.state !== "running") return false;
    const ctx = sharedAudioContext;

    const master = getNotificationVolume();
    const tone = (freq, startAt, duration = 0.16, gainValue = 0.1) => {
      const osc = ctx.createOscillator();
      const gain = ctx.createGain();
      osc.type = "sine";
      osc.frequency.setValueAtTime(freq, startAt);
      gain.gain.setValueAtTime(0.001, startAt);
      gain.gain.exponentialRampToValueAtTime(Math.max(0.001, gainValue * master), startAt + 0.01);
      gain.gain.exponentialRampToValueAtTime(0.001, startAt + duration);
      osc.connect(gain);
      gain.connect(ctx.destination);
      osc.start(startAt);
      osc.stop(startAt + duration + 0.02);
    };

    const now = ctx.currentTime;
    switch (style) {
      case "soft":
        tone(frequency, now, 0.14, 0.07);
        break;
      case "double":
        tone(frequency - 90, now, 0.12, 0.1);
        tone(frequency + 30, now + 0.15, 0.12, 0.1);
        break;
      case "chime":
        tone(frequency - 130, now, 0.13, 0.09);
        tone(frequency, now + 0.12, 0.13, 0.09);
        tone(frequency + 120, now + 0.24, 0.15, 0.09);
        break;
      case "whatsapp":
        tone(740, now, 0.09, 0.16);
        tone(920, now + 0.11, 0.11, 0.18);
        break;
      case "classic":
      default:
        tone(frequency, now, 0.18, 0.12);
        break;
    }
    return true;
  } catch {
    return false;
  }
}
