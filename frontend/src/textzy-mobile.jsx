import { useState, useRef, useEffect, useCallback } from "react";
import * as signalR from "@microsoft/signalr";
import { subscribeFcm, subscribePush, setRuntimePushConfig, getRuntimePushConfig } from "./lib/browserNotifications";
import { getPublicAppUpdateManifest } from "./lib/api";
import {
  C,
  API_BASE,
  SESSION_KEY,
  idempotencyKey,
  resolveCsrf,
  resolveDeviceContext,
  getDeviceLocation,
  apiFetch,
} from "./mobile-shell/core";
import { CONTACTS, REPLIES, I, QA_LIBRARY, EMOJI_SET } from "./mobile-shell/uiAssets";
import { mapConversation, mapMessage, extractTemplateParamIndexes } from "./mobile-shell/conversationMapping";
import ChatComposer from "./mobile-shell/components/ChatComposer";
import { LoginScreen } from "./mobile-shell/components/LoginScreen";
import SharedDialogs from "./mobile-shell/components/SharedDialogs";
import ProjectPicker from "./mobile-shell/components/ProjectPicker";
import { Avatar, Typing } from "./mobile-shell/components/ChatAtoms";
import ProfileView from "./mobile-shell/components/ProfileView";
import ChatView from "./mobile-shell/components/ChatView";

/* ═══════════════════════════════════════════════
   TEXTZY MOBILE — NO BLACK PALETTE
   Orange #F97316  ·  White #FFFFFF
   All dark tones replaced with deep teal/slate
═══════════════════════════════════════════════ */


const renderMessageBody = (msg) => {
  const kind = String(msg?.specialKind || "");
  const data = msg?.specialData || {};

  if (kind === "location") {
    const mapHref = data?.lat && data?.lng
      ? `https://maps.google.com/?q=${encodeURIComponent(`${data.lat},${data.lng}`)}`
      : "";
    return (
      <div style={{ display: "grid", gap: 6 }}>
        <div style={{ fontSize: 12, fontWeight: 700, color: "#0369A1" }}>Location</div>
        <p style={{ margin: 0, fontSize: 15, color: C.textMain, lineHeight: 1.45, wordBreak: "break-word" }}>
          {data?.label || "Shared location"}
        </p>
        {mapHref ? (
          <a
            href={mapHref}
            target="_blank"
            rel="noreferrer"
            style={{ fontSize: 12, color: C.orangeDark, textDecoration: "underline", fontWeight: 600 }}
          >
            Open in Maps
          </a>
        ) : null}
      </div>
    );
  }

  if (kind === "order") {
    return (
      <div style={{ display: "grid", gap: 6 }}>
        <div style={{ fontSize: 12, fontWeight: 700, color: "#4338CA" }}>Order Details</div>
        <p style={{ margin: 0, fontSize: 14, color: C.textMain }}>Catalog: {data?.catalog || "-"}</p>
        <p style={{ margin: 0, fontSize: 14, color: C.textMain }}>Items: {data?.items || "-"}</p>
        {data?.note ? (
          <p style={{ margin: 0, fontSize: 14, color: C.textMain, lineHeight: 1.45, wordBreak: "break-word" }}>
            {data.note}
          </p>
        ) : null}
      </div>
    );
  }

  if (kind === "contacts") {
    return (
      <div style={{ display: "grid", gap: 6 }}>
        <div style={{ fontSize: 12, fontWeight: 700, color: "#047857" }}>Contacts Shared</div>
        <p style={{ margin: 0, fontSize: 14, color: C.textMain }}>{data?.count || 0} contact(s)</p>
      </div>
    );
  }

  if (kind === "reaction") {
    return (
      <div style={{ display: "grid", gap: 6 }}>
        <div style={{ fontSize: 12, fontWeight: 700, color: "#7C3AED" }}>Reaction</div>
        <p style={{ margin: 0, fontSize: 24, lineHeight: 1 }}>{data?.emoji || "?"}</p>
      </div>
    );
  }

  if (kind === "unsupported") {
    return (
      <div style={{ display: "grid", gap: 6 }}>
        <div style={{ fontSize: 12, fontWeight: 700, color: "#B45309" }}>Unsupported Type</div>
        <p style={{ margin: 0, fontSize: 14, color: C.textMain, lineHeight: 1.45, wordBreak: "break-word" }}>
          {data?.reason || msg?.text || ""}
        </p>
      </div>
    );
  }

  return (
    <p style={{ margin: 0, fontSize: 15, color: C.textMain, lineHeight: 1.45, wordBreak: "break-word" }}>
      {msg?.text || ""}
    </p>
  );
};

/* ════════════════════════════
   QR CODE (decorative)
════════════════════════════ */
export default function TextzyMobile() {
  const restored = (() => {
    try {
      return JSON.parse(localStorage.getItem(SESSION_KEY) || "{}");
    } catch {
      return {};
    }
  })();
  const deviceCtxRef = useRef(resolveDeviceContext(restored));
  const [screen, setScreen]   = useState("login");
  const [user, setUser]       = useState(restored.user || null);
  const [project, setProject] = useState(restored.project || null);
  const [projects, setProjects] = useState([]);
  const [session, setSession] = useState({
    csrfToken: restored.csrfToken || "",
    tenantSlug: restored.tenantSlug || "",
  });
  const [contacts, setCons]   = useState([]);
  const [activeId, setAId]    = useState(null);
  const [input, setInput]     = useState("");
  const [search, setSearch]   = useState("");
  const [tab, setTab]         = useState("All");
  const [view, setView]       = useState("list"); // "list" | "chat" | "profile"
  const [showMainMenu, setShowMainMenu] = useState(false);
  const [showChatMenu, setShowChatMenu] = useState(false);
  const [notice, setNotice] = useState("");
  const [showNewChat, setShowNewChat] = useState(false);
  const [newChatRecipient, setNewChatRecipient] = useState("");
  const [newChatBody, setNewChatBody] = useState("Hello");
  const [showTransfer, setShowTransfer] = useState(false);
  const [transferMode, setTransferMode] = useState("transfer");
  const [teamMembers, setTeamMembers] = useState([]);
  const [transferAssignee, setTransferAssignee] = useState("");
  const [showLabelsModal, setShowLabelsModal] = useState(false);
  const [labelsInput, setLabelsInput] = useState("");
  const [showNotesModal, setShowNotesModal] = useState(false);
  const [notesInput, setNotesInput] = useState("");
  const [notes, setNotes] = useState([]);
  const [showQaModal, setShowQaModal] = useState(false);
  const [showTemplateModal, setShowTemplateModal] = useState(false);
  const [updatePrompt, setUpdatePrompt] = useState(null);
  const [templates, setTemplates] = useState([]);
  const [selectedTemplateId, setSelectedTemplateId] = useState("");
  const [templateVars, setTemplateVars] = useState({});
  const [showEmojiPicker, setShowEmojiPicker] = useState(false);
  const [showDevicesModal, setShowDevicesModal] = useState(false);
  const [devices, setDevices] = useState([]);
  const [showSettingsModal, setShowSettingsModal] = useState(false);
  const [showNotificationsModal, setShowNotificationsModal] = useState(false);
  const [notifEnabled, setNotifEnabled] = useState(() => localStorage.getItem("textzy.mobile.notif.enabled") !== "0");
  const [settingsCompact, setSettingsCompact] = useState(() => localStorage.getItem("textzy.mobile.settings.compact") === "1");
  const [settingsSound, setSettingsSound] = useState(() => localStorage.getItem("textzy.mobile.settings.sound") !== "0");
  const [busy, setBusy] = useState({
    project: false,
    send: false,
    newChat: false,
    transfer: false,
    labels: false,
    devices: false,
    notes: false,
    template: false,
  });
  const msgEnd  = useRef(null);
  const unreadTotalRef = useRef(0);
  const signalConnRef = useRef(null);
  const audioCtxRef = useRef(null);
  const inputRef = useRef(null);
  const fileInputRef = useRef(null);
  const typingTimerRef = useRef(null);
  const typingActiveRef = useRef(false);
  const notifyRegisteredRef = useRef(false);
  const runtimePushRef = useRef({
    vapidPublicKey: "",
    firebaseConfig: null,
  });

  const active       = contacts.find(c=>c.id===activeId);
  const unreadCount  = contacts.filter(c=>c.unread>0).length;
  const authCtx = { csrfToken: session.csrfToken, tenantSlug: session.tenantSlug };
  const selectedTemplate = templates.find((t) => String(t.id) === String(selectedTemplateId)) || templates[0] || null;
  const templateParamIndexes = extractTemplateParamIndexes(selectedTemplate?.body || "");
  const activeRecipient = (() => {
    const raw = String(active?.customerPhone || active?.phone || "").trim();
    if (raw) return raw;
    const fromName = String(active?.name || "").replace(/[^\d+]/g, "");
    return fromName.length >= 8 ? fromName : "";
  })();

  const parseErrorText = (raw) => {
    const text = String(raw || "").trim();
    if (!text) return "Something went wrong.";
    try {
      const obj = JSON.parse(text);
      if (obj?.error) return String(obj.error);
      const list = [];
      if (obj?.title) list.push(obj.title);
      if (obj?.errors && typeof obj.errors === "object") {
        Object.values(obj.errors).forEach((v) => {
          if (Array.isArray(v)) list.push(v.join(", "));
        });
      }
      return list.join(" | ") || text;
    } catch {
      return text;
    }
  };

  const openTrustedDownloadUrl = useCallback((rawUrl) => {
    const text = String(rawUrl || "").trim();
    if (!text) {
      setNotice("Download URL is not configured in platform settings.");
      return;
    }
    try {
      const u = new URL(text, window.location.origin);
      const trustedHosts = new Set([
        window.location.hostname,
        "textzy-frontend-production.up.railway.app",
        "textzy-backend-production.up.railway.app",
      ]);
      if (u.protocol !== "https:" || !trustedHosts.has(u.hostname)) {
        setNotice("Blocked untrusted update URL. Please check platform app-update settings.");
        return;
      }
      window.location.assign(u.toString());
    } catch {
      setNotice("Invalid update URL in platform settings.");
    }
  }, []);

  const withBusy = async (key, fn) => {
    if (busy[key]) return;
    setBusy((p) => ({ ...p, [key]: true }));
    try {
      await fn();
    } finally {
      setBusy((p) => ({ ...p, [key]: false }));
    }
  };

  const playNotificationTone = (frequency = 880, durationMs = 140) => {
    if (!notifEnabled) return;
    if (typeof window === "undefined") return;
    const Ctx = window.AudioContext || window.webkitAudioContext;
    if (!Ctx) return;
    try {
      if (!audioCtxRef.current) audioCtxRef.current = new Ctx();
      const ctx = audioCtxRef.current;
      if (ctx.state === "suspended") ctx.resume().catch(() => {});
      const osc = ctx.createOscillator();
      const gain = ctx.createGain();
      osc.type = "sine";
      osc.frequency.value = frequency;
      gain.gain.setValueAtTime(0.0001, ctx.currentTime);
      gain.gain.exponentialRampToValueAtTime(0.08, ctx.currentTime + 0.02);
      gain.gain.exponentialRampToValueAtTime(0.0001, ctx.currentTime + durationMs / 1000);
      osc.connect(gain);
      gain.connect(ctx.destination);
      osc.start();
      osc.stop(ctx.currentTime + durationMs / 1000);
    } catch {
      // no-op
    }
  };

  useEffect(()=>{ msgEnd.current?.scrollIntoView({behavior:"smooth"}); },
    [active?.messages?.length, active?.typing]);

  useEffect(() => {
    localStorage.setItem("textzy.mobile.notif.enabled", notifEnabled ? "1" : "0");
    if (notifEnabled && typeof Notification !== "undefined" && Notification.permission === "default") {
      Notification.requestPermission().catch(() => {});
    }
  }, [notifEnabled]);

  useEffect(() => {
    localStorage.setItem("textzy.mobile.settings.compact", settingsCompact ? "1" : "0");
  }, [settingsCompact]);

  useEffect(() => {
    localStorage.setItem("textzy.mobile.settings.sound", settingsSound ? "1" : "0");
  }, [settingsSound]);

  useEffect(() => {
    setTemplateVars({});
  }, [selectedTemplateId]);

  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => {
    if (!showTemplateModal) return;
    if (templates.length > 0) return;
    loadApprovedTemplates().catch(() => {});
  }, [showTemplateModal]); // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    const deviceCtx = deviceCtxRef.current || resolveDeviceContext(restored);
    const platform = String(deviceCtx.devicePlatform || "android").toLowerCase();
    const appVersion = String(deviceCtx.appVersion || "1.0.0");
    getPublicAppUpdateManifest({ platform, appVersion })
      .then((manifest) => {
        const current = manifest?.current || null;
        const platformNode = manifest?.platforms?.[platform] || {};
        const downloadUrl = platformNode?.downloadUrl || "";
        if (!current?.updateAvailable) return;
        setUpdatePrompt({
          forceUpdate: !!current.forceUpdate,
          appVersion,
          latestVersion: platformNode?.latestVersion || "",
          downloadUrl,
        });
      })
      .catch(() => {});
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  const persistSession = (nextSession, nextUser = user, nextProject = project) => {
    const deviceCtx = deviceCtxRef.current || resolveDeviceContext(restored);
    localStorage.setItem(SESSION_KEY, JSON.stringify({
      csrfToken: nextSession.csrfToken,
      tenantSlug: nextSession.tenantSlug || "",
      installId: deviceCtx.installId || "",
      user: nextUser || null,
      project: nextProject || null,
    }));
  };

  const loadProjects = async () => {
    const { res } = await apiFetch("/api/auth/projects");
    if (!res.ok) throw new Error(await res.text() || "Failed to load projects");
    const rows = await res.json();
    const mapped = (rows || []).map((p, idx) => ({
      slug: p.slug || p.Slug,
      name: p.name || p.Name,
      role: p.role || p.Role || "agent",
      icon: ["MA", "TC", "RH", "PR", "TM"][idx % 5],
    }));
    setProjects(mapped);
    return mapped;
  };

  const loadConversations = async (ctx = authCtx) => {
    if (!ctx.tenantSlug) return;
    const { res } = await apiFetch("/api/inbox/conversations?take=100", {
      tenantSlug: ctx.tenantSlug,
    });
    if (!res.ok) throw new Error(await res.text() || "Failed to load conversations");
    const rows = await res.json();
    setCons((prev) => (rows || []).map((r) => {
      const mapped = mapConversation(r);
      const existing = prev.find((c) => String(c.id) === String(mapped.id));
      if (!existing) return mapped;
      return {
        ...mapped,
        // Preserve loaded chat history so list refresh does not blank chat view.
        messages: existing.messages || [],
        typing: existing.typing || false,
      };
    }));
  };

  const loadMessages = async (conversationId, ctx = authCtx) => {
    const { res } = await apiFetch(`/api/inbox/conversations/${conversationId}/messages?take=80`, {
      tenantSlug: ctx.tenantSlug,
    });
    if (!res.ok) throw new Error(await res.text() || "Failed to load messages");
    const rows = await res.json();
    const mapped = (rows || []).map(mapMessage);
    setCons((prev) => prev.map((c) => {
      if (c.id !== conversationId) return c;
      if (mapped.length === 0 && (c.messages || []).length > 0) return c;
      return { ...c, messages: mapped };
    }));
  };

  const loadTeamMembers = async (ctx = authCtx) => {
    const { res } = await apiFetch("/api/auth/team-members", {
      tenantSlug: ctx.tenantSlug,
    });
    if (!res.ok) return [];
    const rows = await res.json().catch(() => []);
    return Array.isArray(rows) ? rows : [];
  };

  const loadDevices = async (ctx = authCtx) => {
    const { res } = await apiFetch("/api/auth/devices", {
      tenantSlug: ctx.tenantSlug,
    });
    if (!res.ok) throw new Error(await res.text() || "Failed to load devices");
    const rows = await res.json().catch(() => []);
    setDevices(Array.isArray(rows) ? rows : []);
  };

  const loadNotes = async (conversationId, ctx = authCtx) => {
    if (!conversationId) return [];
    const { res } = await apiFetch(`/api/inbox/conversations/${conversationId}/notes?take=50`, {
      tenantSlug: ctx.tenantSlug,
    });
    if (!res.ok) throw new Error(await res.text() || "Failed to load notes");
    const rows = await res.json().catch(() => []);
    const mapped = Array.isArray(rows) ? rows : [];
    setNotes(mapped);
    return mapped;
  };

  const loadAppBootstrap = async (ctx = authCtx) => {
    const { res } = await apiFetch("/api/auth/app-bootstrap", {
      tenantSlug: ctx.tenantSlug,
    });
    if (!res.ok) throw new Error(await res.text() || "Failed to load app bootstrap");
    const json = await res.json().catch(() => ({}));
    const app = json?.app || {};
    const runtimeCfg = {
      vapidPublicKey:
        String(
          app.webPushPublicKey ||
          app.vapidPublicKey ||
          process.env.REACT_APP_WEB_PUSH_PUBLIC_KEY ||
          process.env.VITE_WEB_PUSH_PUBLIC_KEY ||
          ""
        ).trim(),
      firebaseConfig: {
        apiKey: String(app.firebaseApiKey || "").trim(),
        authDomain: String(app.firebaseAuthDomain || "").trim(),
        projectId: String(app.firebaseProjectId || "").trim(),
        storageBucket: String(app.firebaseStorageBucket || "").trim(),
        messagingSenderId: String(app.firebaseMessagingSenderId || "").trim(),
        appId: String(app.firebaseAppId || "").trim(),
        measurementId: String(app.firebaseMeasurementId || "").trim(),
      },
    };
    runtimePushRef.current = runtimeCfg;
    setRuntimePushConfig(runtimeCfg);
    return json;
  };

  const loadApprovedTemplates = async (ctx = authCtx) => {
    const { res } = await apiFetch("/api/templates", {
      tenantSlug: ctx.tenantSlug,
    });
    if (!res.ok) throw new Error(await res.text() || "Failed to load templates");
    const rows = await res.json().catch(() => []);
    const approved = (Array.isArray(rows) ? rows : []).filter((x) =>
      String(x.status || x.Status || "").toLowerCase() === "approved" &&
      Number(x.channel || x.Channel || 0) === 2
    );
    setTemplates(approved);
    if (approved.length > 0 && !selectedTemplateId) {
      setSelectedTemplateId(String(approved[0].id || approved[0].Id));
    }
    return approved;
  };

  const ensureNotificationSubscription = async (ctx = authCtx) => {
    if (notifyRegisteredRef.current || !notifEnabled) return;
    if (typeof Notification === "undefined" || Notification.permission !== "granted") return;
    const runtimeCfg = getRuntimePushConfig();
    const vapid =
      String(runtimeCfg?.vapidPublicKey || runtimePushRef.current?.vapidPublicKey || "").trim() ||
      process.env.REACT_APP_WEB_PUSH_PUBLIC_KEY ||
      process.env.VITE_WEB_PUSH_PUBLIC_KEY ||
      "";
    if (!vapid) return;
    try {
      const firebaseConfig = runtimeCfg?.firebaseConfig || runtimePushRef.current?.firebaseConfig || null;
      const fcmToken = await subscribeFcm(vapid, firebaseConfig);
      if (fcmToken) {
        const fcmRes = await apiFetch("/api/notifications/subscriptions", {
          method: "POST",
          tenantSlug: ctx.tenantSlug,
          csrfToken: ctx.csrfToken,
          body: {
            provider: "fcm",
            endpoint: fcmToken,
            p256dh: "",
            auth: "",
            userAgent: typeof navigator !== "undefined" ? navigator.userAgent : "",
          },
        });
        if (fcmRes.res.ok) {
          notifyRegisteredRef.current = true;
          return;
        }
      }

      const webPush = await subscribePush(vapid);
      if (!webPush?.endpoint) return;
      const keys = webPush.keys || {};
      const webPushRes = await apiFetch("/api/notifications/subscriptions", {
        method: "POST",
        tenantSlug: ctx.tenantSlug,
        csrfToken: ctx.csrfToken,
        body: {
          provider: "webpush",
          endpoint: webPush.endpoint,
          p256dh: keys.p256dh || "",
          auth: keys.auth || "",
          userAgent: typeof navigator !== "undefined" ? navigator.userAgent : "",
        },
      });
      if (webPushRes.res.ok) notifyRegisteredRef.current = true;
    } catch {
      // best-effort registration
    }
  };

  const isWithin24HourWindow = (conversation) => {
    if (!conversation) return true;
    const inbound = (conversation.messages || [])
      .filter((m) => !m.sent && typeof m.createdAtMs === "number")
      .sort((a, b) => (b.createdAtMs || 0) - (a.createdAtMs || 0));
    if (inbound.length === 0) return true;
    const age = Date.now() - inbound[0].createdAtMs;
    return age <= 24 * 60 * 60 * 1000;
  };

  const handleLogin = async (payload) => {
    if (payload?.mode === "request-otp") {
      const { res } = await apiFetch("/api/auth/email-verification/request", {
        method: "POST",
        body: { email: payload.email, purpose: "login" },
      });
      if (!res.ok) throw new Error(await res.text() || "Failed to send OTP.");
      return await res.json();
    }

    if (payload?.mode === "otp-status") {
      const id = encodeURIComponent(payload.verificationId || "");
      const purpose = encodeURIComponent("login");
      const email = encodeURIComponent(payload.email || "");
      const query = email
        ? `/api/auth/email-verification/status?verificationId=${id}&purpose=${purpose}&email=${email}`
        : `/api/auth/email-verification/status?verificationId=${id}&purpose=${purpose}`;
      const { res } = await apiFetch(query, { method: "GET" });
      if (!res.ok) throw new Error(await res.text() || "Failed to read verification status.");
      return await res.json();
    }

    if (payload?.mode === "verify-otp") {
      const { res } = await apiFetch("/api/auth/email-verification/verify", {
        method: "POST",
        body: { email: payload.email, purpose: "login", verificationId: payload.verificationId, otp: payload.otp },
      });
      if (!res.ok) throw new Error(await res.text() || "Invalid OTP.");
      return await res.json();
    }

    if (payload?.mode === "qr") {
      const deviceCtx = deviceCtxRef.current || resolveDeviceContext(restored);
      const location = await getDeviceLocation();
      const { res, nextCsrfHeader } = await apiFetch("/api/public/mobile/pair/exchange", {
        method: "POST",
        body: {
          pairingToken: payload.pairingToken,
          installId: deviceCtx.installId,
          deviceName: deviceCtx.deviceName,
          devicePlatform: deviceCtx.devicePlatform,
          deviceModel: deviceCtx.deviceModel,
          osVersion: deviceCtx.osVersion,
          appVersion: deviceCtx.appVersion,
          locationLat: location?.latitude ?? null,
          locationLng: location?.longitude ?? null,
          locationAccuracyMeters: location?.accuracyMeters ?? null,
          locationCapturedAtUtc: location?.capturedAtUtc ?? null,
        },
      });
      if (!res.ok) throw new Error(await res.text() || "Pairing code expired or invalid.");
      const json = await res.json().catch(() => ({}));
      const csrfToken = resolveCsrf(json.csrfToken || json.CsrfToken || nextCsrfHeader);
      const tenantSlug =
        json.tenantSlug ||
        json.TenantSlug ||
        json.projectSlug ||
        json.ProjectSlug ||
        json.project?.slug ||
        json.Project?.Slug ||
        json.tenant?.slug ||
        json.Tenant?.Slug ||
        "";
      const loggedUser = json.user || json.User || { email: "mobile@textzy.io" };
      const nextSession = { csrfToken, tenantSlug };
      setSession(nextSession);
      setUser(loggedUser);
      await loadAppBootstrap(nextSession).catch(() => null);
      const projList = await loadProjects().catch(() => []);
      if (tenantSlug) {
        const selected = projList.find((p) => String(p.slug).toLowerCase() === String(tenantSlug).toLowerCase()) || {
          slug: tenantSlug,
          name: tenantSlug.charAt(0).toUpperCase() + tenantSlug.slice(1),
          role: "agent",
          icon: "PR",
        };
        setProject(selected);
        setScreen("app");
        persistSession(nextSession, loggedUser, selected);
        await loadConversations(nextSession);
      } else {
        setScreen("project");
        persistSession(nextSession, loggedUser, null);
      }
      return;
    }

    if (payload?.mode === "verify-authenticator") {
      const { res, nextCsrfHeader } = await apiFetch("/api/auth/two-factor/verify-login", {
        method: "POST",
        body: { challengeToken: payload.challengeToken, code: payload.code },
      });
      if (!res.ok) throw new Error(await res.text() || "Authenticator code is invalid.");
      const json = await res.json().catch(() => ({}));
      const csrfToken = resolveCsrf(json.csrfToken || json.CsrfToken || nextCsrfHeader);
      const nextSession = { csrfToken, tenantSlug: "" };
      const nextUser = { email: payload.email };
      setSession(nextSession);
      setUser(nextUser);
      await loadAppBootstrap(nextSession).catch(() => null);
      await loadProjects();
      setScreen("project");
      persistSession(nextSession, nextUser, null);
      return { ok: true };
    }

    const { res, nextCsrfHeader } = await apiFetch("/api/auth/login", {
      method: "POST",
      body: { email: payload.email, password: payload.password, emailVerificationId: payload.emailVerificationId || "" },
    });
    if (!res.ok) throw new Error(await res.text() || "Invalid credentials");
    const json = await res.json().catch(() => ({}));
    if (json?.requiresTwoFactor) return json;
    const csrfToken = resolveCsrf(json.csrfToken || json.CsrfToken || nextCsrfHeader);
    const nextSession = { csrfToken, tenantSlug: "" };
    const nextUser = { email: payload.email };
    setSession(nextSession);
    setUser(nextUser);
    await loadAppBootstrap(nextSession).catch(() => null);
    await loadProjects();
    setScreen("project");
    persistSession(nextSession, nextUser, null);
    return { ok: true };
  };

  const handleSelectProject = async (p) => {
    return withBusy("project", async () => {
    let workingCsrf = resolveCsrf(session.csrfToken);

    // Always refresh before switch to ensure CSRF cookie/header are synced in WebView.
    const refreshed = await apiFetch("/api/auth/refresh", {
      method: "POST",
    });
    if (refreshed.res.ok) {
      const refreshedBody = await refreshed.res.json().catch(() => ({}));
      workingCsrf = resolveCsrf(refreshedBody.csrfToken || refreshedBody.CsrfToken || refreshed.nextCsrfHeader || workingCsrf);
      setSession((prev) => ({ ...prev, csrfToken: workingCsrf }));
    }

    const trySwitch = async (csrfToken) => apiFetch("/api/auth/switch-project", {
      method: "POST",
      csrfToken: resolveCsrf(csrfToken),
      body: { slug: p.slug },
    });

    let first = await trySwitch(workingCsrf);
    let res = first.res;
    let nextCsrfHeader = first.nextCsrfHeader;

    if (!res.ok) {
      const errText = await res.text();
      if (res.status === 403 && errText.toLowerCase().includes("csrf")) {
        const refreshed = await apiFetch("/api/auth/refresh", {
          method: "POST",
        });
        if (refreshed.res.ok) {
          const refreshedBody = await refreshed.res.json().catch(() => ({}));
          const refreshedCsrf = resolveCsrf(refreshedBody.csrfToken || refreshedBody.CsrfToken || refreshed.nextCsrfHeader || session.csrfToken);
          setSession((prev) => ({ ...prev, csrfToken: refreshedCsrf }));
          first = await trySwitch(refreshedCsrf);
          res = first.res;
          nextCsrfHeader = first.nextCsrfHeader || refreshedCsrf;
        }
      }
      if (!res.ok) throw new Error((await res.text()) || "Project switch failed");
    }

    const json = await res.json().catch(() => ({}));
    const csrfToken = resolveCsrf(nextCsrfHeader || session.csrfToken);
    const tenantSlug = json.tenantSlug || json.TenantSlug || p.slug;
    const selectedProject = { ...p, role: json.role || json.Role || p.role };
    const nextSession = { csrfToken, tenantSlug };
    setSession(nextSession);
    setProject(selectedProject);
    setScreen("app");
    setAId(null);
    setInput("");
    await loadAppBootstrap(nextSession).catch(() => null);
    persistSession(nextSession, user, selectedProject);
    await loadConversations(nextSession);
    });
  };

  const openChat = async (id) => {
    setAId(id); setView("chat");
    setShowEmojiPicker(false);
    setCons(p=>p.map(c=>c.id===id?{...c,unread:0}:c));
    try {
      await Promise.all([loadMessages(id), loadNotes(id).catch(() => [])]);
    } catch {
      // keep old local state
    }
    setTimeout(()=>inputRef.current?.focus(),150);
  };

  const handleEmoji = () => {
    setShowEmojiPicker((v) => !v);
  };

  const handleAttachClick = () => {
    fileInputRef.current?.click();
  };

  const handleMicInput = () => {
    const SR = window.SpeechRecognition || window.webkitSpeechRecognition;
    if (!SR) {
      setNotice("Voice input is not supported on this device.");
      return;
    }
    const rec = new SR();
    rec.lang = "en-US";
    rec.interimResults = false;
    rec.maxAlternatives = 1;
    rec.onresult = (e) => {
      const spoken = e?.results?.[0]?.[0]?.transcript || "";
      if (spoken) setInput((prev) => `${prev}${prev ? " " : ""}${spoken}`);
      setTimeout(()=>inputRef.current?.focus(), 60);
    };
    rec.onerror = () => {
      setNotice("Could not capture voice. Please try again.");
    };
    rec.start();
  };

  const handleAttachmentSelected = async (e) => {
    const file = e.target.files?.[0];
    e.target.value = "";
    if (!file) return;
    if (!activeId) {
      setNotice("Open a chat before attaching a file.");
      return;
    }
    if (!activeRecipient) {
      setNotice("Recipient phone is missing for this conversation.");
      return;
    }
    const formData = new FormData();
    formData.append("recipient", activeRecipient);
    formData.append("file", file);
    if (file.type.startsWith("image/")) formData.append("mediaType", "image");
    else if (file.type.startsWith("video/")) formData.append("mediaType", "video");
    else if (file.type.startsWith("audio/")) formData.append("mediaType", "audio");
    else formData.append("mediaType", "document");

    const { res } = await apiFetch("/api/messages/upload-whatsapp-media", {
      method: "POST",
      tenantSlug: authCtx.tenantSlug,
      csrfToken: authCtx.csrfToken,
      body: formData,
      extraHeaders: { "Idempotency-Key": idempotencyKey() },
    });
    if (!res.ok) {
      const err = await res.text();
      setNotice(parseErrorText(err) || "Attachment send failed.");
      return;
    }
    await loadMessages(activeId);
    await loadConversations();
  };

  const openMessageMedia = async (msg) => {
    const mediaId = msg?.media?.mediaId;
    if (!mediaId) {
      setNotice("Media file is not available.");
      return;
    }
    const headers = {};
    if (authCtx.tenantSlug) headers["X-Tenant-Slug"] = authCtx.tenantSlug;
    const res = await fetch(`${API_BASE}/api/messages/media/${encodeURIComponent(mediaId)}`, {
      headers,
      credentials: "include",
    });
    if (!res.ok) {
      setNotice("Unable to open media.");
      return;
    }
    const blob = await res.blob();
    const url = URL.createObjectURL(blob);
    window.open(url, "_blank", "noopener,noreferrer");
    setTimeout(() => URL.revokeObjectURL(url), 60_000);
  };

  const sendTyping = (isTyping) => {
    if (!activeId || !authCtx.tenantSlug) return;
    apiFetch("/api/inbox/typing", {
      method: "POST",
      tenantSlug: authCtx.tenantSlug,
      csrfToken: authCtx.csrfToken,
      body: { conversationId: activeId, isTyping },
    }).catch(() => {});
  };

  const onInputTyping = (nextValue) => {
    setInput(nextValue);
    if (!activeId) return;
    if (!typingActiveRef.current) {
      typingActiveRef.current = true;
      sendTyping(true);
    }
    if (typingTimerRef.current) clearTimeout(typingTimerRef.current);
    typingTimerRef.current = setTimeout(() => {
      typingActiveRef.current = false;
      sendTyping(false);
    }, 1200);
  };

  const stopTypingNow = () => {
    if (typingTimerRef.current) clearTimeout(typingTimerRef.current);
    if (!typingActiveRef.current) return;
    typingActiveRef.current = false;
    sendTyping(false);
  };

  const send = async () => {
    await withBusy("send", async () => {
      const txt = input.trim();
      if (!txt||!activeId) return;
      if (!activeRecipient) {
        setNotice("Recipient phone is missing for this conversation.");
        return;
      }
      if (!isWithin24HourWindow(active)) {
        setShowTemplateModal(true);
        await loadApprovedTemplates().catch(() => {});
        return;
      }
      const m = {id:Date.now(),text:txt,sent:true,time:new Date().toLocaleTimeString([],{hour:"2-digit",minute:"2-digit"}),status:"sent"};
      setCons(p=>p.map(c=>c.id===activeId?{...c,messages:[...c.messages,m],lastMsg:txt,time:m.time,unread:0}:c));
      setInput("");
      setShowEmojiPicker(false);
      stopTypingNow();
      try {
        const { res } = await apiFetch("/api/messages/send", {
          method: "POST",
          tenantSlug: authCtx.tenantSlug,
          csrfToken: authCtx.csrfToken,
          body: {
            recipient: activeRecipient,
            body: txt,
            channel: 2,
            idempotencyKey: idempotencyKey(),
          },
        });
        if (!res.ok) {
          const err = await res.text();
          setNotice(parseErrorText(err) || "Message send failed.");
          return;
        }
        await Promise.all([loadMessages(activeId), loadConversations()]);
        setTimeout(() => {
          loadMessages(activeId).catch(() => {});
          loadConversations().catch(() => {});
        }, 1200);
      } catch (e) {
        setNotice(e?.message || "Message send failed.");
      }
    });
  };

  const handleTransferConversation = async () => {
    if (!activeId) return;
    const members = await loadTeamMembers();
    setTeamMembers(members);
    setTransferMode("transfer");
    setTransferAssignee("");
    setShowTransfer(true);
    setShowChatMenu(false);
  };

  const handleAssignConversation = async () => {
    if (!activeId) return;
    const members = await loadTeamMembers();
    setTeamMembers(members);
    setTransferMode("assign");
    setTransferAssignee("");
    setShowTransfer(true);
    setShowChatMenu(false);
  };

  const submitTransferConversation = async () => {
    await withBusy("transfer", async () => {
      const fallback = transferAssignee.trim();
      let picked = null;
      if (teamMembers.length > 0) {
        picked = teamMembers.find((m) => String(m.fullName || m.email || "").toLowerCase() === String(fallback || "").toLowerCase())
          || teamMembers.find((m) => String(m.email || "").toLowerCase() === String(fallback || "").toLowerCase());
      }
      const userName = picked?.fullName || picked?.name || fallback || "Agent";
      const userId = picked?.id || picked?.userId || null;
      const endpoint = transferMode === "assign"
        ? `/api/inbox/conversations/${activeId}/assign`
        : `/api/inbox/conversations/${activeId}/transfer`;
      const { res } = await apiFetch(endpoint, {
        method: "POST",
        tenantSlug: authCtx.tenantSlug,
        csrfToken: authCtx.csrfToken,
        body: { userId, userName },
      });
      if (!res.ok) {
        setNotice(parseErrorText(await res.text()) || (transferMode === "assign" ? "Assign failed" : "Transfer failed"));
        return;
      }
      await loadConversations();
      await loadMessages(activeId);
      setShowTransfer(false);
    });
  };

  const handleSetLabels = async () => {
    if (!activeId) return;
    setLabelsInput("");
    setShowLabelsModal(true);
    setShowChatMenu(false);
  };

  const submitLabels = async () => {
    await withBusy("labels", async () => {
      const labels = labelsInput.split(",").map((x) => x.trim()).filter(Boolean);
      const { res } = await apiFetch(`/api/inbox/conversations/${activeId}/labels`, {
        method: "POST",
        tenantSlug: authCtx.tenantSlug,
        csrfToken: authCtx.csrfToken,
        body: { labels },
      });
      if (!res.ok) {
        setNotice(parseErrorText(await res.text()) || "Labels update failed");
        return;
      }
      await loadConversations();
      await loadMessages(activeId);
      setShowLabelsModal(false);
    });
  };

  const handleNotes = async () => {
    if (!activeId) return;
    setShowChatMenu(false);
    setShowNotesModal(true);
    await loadNotes(activeId).catch(() => {});
  };

  const submitNote = async () => {
    await withBusy("notes", async () => {
      const body = notesInput.trim();
      if (!body || !activeId) return;
      const { res } = await apiFetch(`/api/inbox/conversations/${activeId}/notes`, {
        method: "POST",
        tenantSlug: authCtx.tenantSlug,
        csrfToken: authCtx.csrfToken,
        body: { body },
      });
      if (!res.ok) {
        setNotice(parseErrorText(await res.text()) || "Note save failed");
        return;
      }
      setNotesInput("");
      await loadNotes(activeId).catch(() => {});
      await loadConversations().catch(() => {});
    });
  };

  const handleToggleStar = async () => {
    if (!activeId) return;
    const current = active?.labels || [];
    const has = current.some((x) => String(x).toLowerCase() === "starred");
    const labels = has ? current.filter((x) => String(x).toLowerCase() !== "starred") : [...current, "starred"];
    const { res } = await apiFetch(`/api/inbox/conversations/${activeId}/labels`, {
      method: "POST",
      tenantSlug: authCtx.tenantSlug,
      csrfToken: authCtx.csrfToken,
      body: { labels },
    });
    if (!res.ok) {
      setNotice(parseErrorText(await res.text()) || "Failed to update star");
      return;
    }
    setShowChatMenu(false);
    await loadConversations();
    await loadMessages(activeId);
  };

  const sendTemplateFallback = async () => {
    await withBusy("template", async () => {
      const tpl = selectedTemplate;
      if (!tpl || !activeRecipient) {
        setNotice("No approved WhatsApp template available.");
        return;
      }
      const params = templateParamIndexes.map((idx) => String(templateVars[idx] || "").trim());
      if (params.some((x) => !x)) {
        setNotice("Fill all template variables.");
        return;
      }
      const { res } = await apiFetch("/api/messages/send", {
        method: "POST",
        tenantSlug: authCtx.tenantSlug,
        csrfToken: authCtx.csrfToken,
        body: {
          recipient: activeRecipient,
          body: String(tpl.body || "Template message"),
          channel: 2,
          useTemplate: true,
          templateName: tpl.name || tpl.Name,
          templateLanguageCode: tpl.language || tpl.Language || "en",
          templateParameters: params,
          idempotencyKey: idempotencyKey(),
        },
      });
      if (!res.ok) {
        setNotice(parseErrorText(await res.text()) || "Template send failed");
        return;
      }
      setShowTemplateModal(false);
      setTemplateVars({});
      await Promise.all([loadMessages(activeId), loadConversations()]);
    });
  };

  const handleNewChat = async () => {
    setNewChatRecipient("");
    setNewChatBody("Hello");
    setShowNewChat(true);
  };

  const submitNewChat = async () => {
    await withBusy("newChat", async () => {
      const recipient = newChatRecipient.trim();
      if (!recipient) {
        setNotice("WhatsApp number is required.");
        return;
      }
      const body = newChatBody.trim() || "Hello";
      const { res } = await apiFetch("/api/messages/send", {
        method: "POST",
        tenantSlug: authCtx.tenantSlug,
        csrfToken: authCtx.csrfToken,
        body: {
          recipient,
          body,
          channel: 2,
          idempotencyKey: idempotencyKey(),
        },
      });
      if (!res.ok) {
        setNotice(parseErrorText(await res.text()) || "Unable to start chat");
        return;
      }
      setShowNewChat(false);
      await loadConversations();
    });
  };

  const openProjectSwitch = async () => {
    setShowMainMenu(false);
    try {
      await loadProjects();
      setScreen("project");
    } catch (e) {
      setNotice(e?.message || "Unable to load projects");
    }
  };

  const openDevicesModal = async () => {
    setShowMainMenu(false);
    setShowDevicesModal(true);
    await withBusy("devices", async () => {
      try {
        await loadDevices();
      } catch (e) {
        setNotice(e?.message || "Unable to load linked devices.");
      }
    });
  };

  const revokeDevice = async (deviceId) => {
    if (!deviceId) return;
    await withBusy("devices", async () => {
      const { res } = await apiFetch(`/api/auth/devices/${deviceId}`, {
        method: "DELETE",
        tenantSlug: authCtx.tenantSlug,
        csrfToken: authCtx.csrfToken,
      });
      if (!res.ok) {
        setNotice(parseErrorText(await res.text()) || "Unable to revoke device.");
        return;
      }
      await loadDevices();
    });
  };

  const filtered = contacts.filter(c=>{
    const q=search.toLowerCase();
    const m=c.name.toLowerCase().includes(q)||c.lastMsg.toLowerCase().includes(q);
    if(tab==="Unread")   return m&&c.unread>0;
    if(tab==="Assigned") return m&&!!c.assignedUserId;
    if(tab==="Starred")  return m&&(c.labels || []).some((l)=>String(l).toLowerCase()==="starred");
    return m;
  });

  useEffect(() => {
    if (screen !== "app") return;
    if (!authCtx.tenantSlug) return;
    if (contacts.length > 0) return;
    loadConversations().catch(() => setCons(CONTACTS));
  }, [screen, authCtx.tenantSlug]); // eslint-disable-line react-hooks/exhaustive-deps

  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => {
    if (!restored.csrfToken && !restored.tenantSlug) return;
    if (restored.tenantSlug) {
      setScreen("app");
      loadConversations({
        csrfToken: restored.csrfToken || "",
        tenantSlug: restored.tenantSlug,
      }).catch(() => setScreen("project"));
      return;
    }
    loadProjects()
      .then(() => setScreen("project"))
      .catch(() => {
        localStorage.removeItem(SESSION_KEY);
      });
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => {
    if (screen !== "app") return;
    if (!authCtx.tenantSlug) return;
    const tick = () => {
      loadConversations().catch(() => {});
      if (view === "chat" && activeId) {
        loadMessages(activeId).catch(() => {});
        loadNotes(activeId).catch(() => {});
      }
    };
    const timer = setInterval(tick, 3000);
    return () => clearInterval(timer);
  }, [screen, authCtx.tenantSlug, view, activeId]); // eslint-disable-line react-hooks/exhaustive-deps

  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => {
    if (screen !== "app") return;
    if (!authCtx.tenantSlug) return;
    loadAppBootstrap(authCtx).catch(() => {});
  }, [screen, authCtx.tenantSlug]); // eslint-disable-line react-hooks/exhaustive-deps

  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => {
    if (screen !== "app") return;
    if (!authCtx.tenantSlug) return;
    ensureNotificationSubscription().catch(() => {});
  }, [screen, authCtx.tenantSlug, notifEnabled]); // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    return () => {
      if (typingTimerRef.current) clearTimeout(typingTimerRef.current);
    };
  }, []);

  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => {
    if (view !== "chat") stopTypingNow();
  }, [view, activeId]); // eslint-disable-line react-hooks/exhaustive-deps

  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => {
    if (screen !== "app") return;
    if (!authCtx.tenantSlug) return;
    const runtimeConfig = typeof window !== "undefined" ? (window.__APP_CONFIG__ || {}) : {};
    const baseUrl =
      runtimeConfig.API_BASE ||
      process.env.REACT_APP_API_BASE ||
      process.env.VITE_API_BASE ||
      "https://textzy-backend-production.up.railway.app";

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`${baseUrl}/hubs/inbox?tenantSlug=${encodeURIComponent(authCtx.tenantSlug)}`, {
        withCredentials: true,
      })
      .withAutomaticReconnect()
      .build();
    signalConnRef.current = connection;

    const refresh = () => {
      loadConversations().catch(() => {});
      if (view === "chat" && activeId) {
        loadMessages(activeId).catch(() => {});
        loadNotes(activeId).catch(() => {});
      }
    };

    connection.on("message.queued", () => refresh());
    connection.on("message.sent", () => refresh());
    connection.on("webhook.inbound", () => {
      refresh();
      playNotificationTone(760, 170);
    });
    connection.on("conversation.assigned", () => refresh());
    connection.on("conversation.transferred", () => refresh());
    connection.on("conversation.labels", () => refresh());
    connection.on("conversation.note", () => refresh());
    connection.on("conversation.typing", (e) => {
      const conversationId = e?.conversationId ?? e?.ConversationId;
      const isTyping = Boolean(e?.isTyping ?? e?.IsTyping);
      setCons((prev) => prev.map((c) => (
        String(c.id) === String(conversationId)
          ? { ...c, typing: isTyping }
          : c
      )));
    });
    connection.onreconnected(() => {
      connection.invoke("JoinTenantRoom", authCtx.tenantSlug).catch(() => {});
      refresh();
    });

    connection.start()
      .then(() => {
        connection.invoke("JoinTenantRoom", authCtx.tenantSlug).catch(() => {});
      })
      .catch(() => {});

    return () => {
      signalConnRef.current = null;
      if (
        connection.state === signalR.HubConnectionState.Connected ||
        connection.state === signalR.HubConnectionState.Reconnecting
      ) {
        connection.stop().catch(() => {});
      }
    };
  }, [screen, authCtx.tenantSlug, view, activeId]); // eslint-disable-line react-hooks/exhaustive-deps

  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => {
    const total = contacts.reduce((sum, c) => sum + (Number(c.unread) || 0), 0);
    const prev = unreadTotalRef.current;
    if (screen === "app" && notifEnabled && total > prev) {
      if (typeof navigator !== "undefined" && typeof navigator.vibrate === "function") {
        navigator.vibrate(120);
      }
      playNotificationTone(880, 150);
      if (typeof Notification !== "undefined" && Notification.permission === "granted") {
        try {
          const diff = total - prev;
          new Notification("Textzy", { body: `${diff} new message${diff > 1 ? "s" : ""}` });
        } catch {
          // Ignore notification errors in embedded webviews.
        }
      }
    }
    unreadTotalRef.current = total;
  }, [contacts, screen, notifEnabled]); // eslint-disable-line react-hooks/exhaustive-deps

  const sharedDialogsProps = {
    C,
    notice,
    setNotice,
    updatePrompt,
    setUpdatePrompt,
    openTrustedDownloadUrl,
    showNewChat,
    newChatRecipient,
    setNewChatRecipient,
    newChatBody,
    setNewChatBody,
    setShowNewChat,
    busy,
    submitNewChat,
    showTransfer,
    transferMode,
    transferAssignee,
    setTransferAssignee,
    teamMembers,
    setShowTransfer,
    submitTransferConversation,
    showLabelsModal,
    labelsInput,
    setLabelsInput,
    setShowLabelsModal,
    submitLabels,
    showNotesModal,
    notesInput,
    setNotesInput,
    setShowNotesModal,
    submitNote,
    notes,
    showTemplateModal,
    templates,
    selectedTemplateId,
    setSelectedTemplateId,
    setTemplateVars,
    templateParamIndexes,
    templateVars,
    setShowTemplateModal,
    sendTemplateFallback,
    showQaModal,
    QA_LIBRARY,
    setInput,
    setShowQaModal,
    inputRef,
    showDevicesModal,
    devices,
    revokeDevice,
    openDevicesModal,
    setShowDevicesModal,
    showSettingsModal,
    settingsCompact,
    setSettingsCompact,
    settingsSound,
    setSettingsSound,
    setShowSettingsModal,
    showNotificationsModal,
    notifEnabled,
    setNotifEnabled,
    setShowNotificationsModal,
  };
  if (screen==="login")   return <><LoginScreen onLogin={handleLogin}/><SharedDialogs {...sharedDialogsProps} /></>;
  if (screen==="project") return <><ProjectPicker projects={projects} loading={busy.project} onSelect={async (p) => {
    try {
      await handleSelectProject(p);
    } catch (e) {
      setNotice(e?.message || "Project switch failed");
    }
  }}/><SharedDialogs {...sharedDialogsProps} /></>;

  const uname=(user?.email||"User").split("@")[0];
  const projectBadge = (() => {
    const icon = String(project?.icon || "").trim();
    if (/^[A-Za-z0-9]{1,4}$/.test(icon)) return icon.toUpperCase();
    const src = String(project?.name || project?.slug || "PR");
    const initials = src
      .split(/[\s_-]+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((s) => s[0]?.toUpperCase() || "")
      .join("");
    return initials || "PR";
  })();

  /* ── PROFILE PANEL ── */
  if (view==="profile") return (
    <ProfileView
      C={C}
      I={I}
      Avatar={Avatar}
      uname={uname}
      projectBadge={projectBadge}
      user={user}
      project={project}
      contacts={contacts}
      setView={setView}
      setTab={setTab}
      setAId={setAId}
      handleSetLabels={handleSetLabels}
      openDevicesModal={openDevicesModal}
      setShowSettingsModal={setShowSettingsModal}
      setShowNotificationsModal={setShowNotificationsModal}
      openProjectSwitch={openProjectSwitch}
      onLogout={() => {
        localStorage.removeItem(SESSION_KEY);
        setSession({ csrfToken: "", tenantSlug: "" });
        setProjects([]);
        setProject(null);
        setUser(null);
        setCons([]);
        setAId(null);
        setInput("");
        setSearch("");
        setTab("All");
        setScreen("login");
        setView("list");
      }}
      sharedDialogsNode={<SharedDialogs {...sharedDialogsProps} />}
    />
  );
  /* ── CHAT VIEW ── */
  if (view==="chat" && active) return (
    <ChatView
      C={C}
      I={I}
      Avatar={Avatar}
      Typing={Typing}
      active={active}
      setView={setView}
      setShowChatMenu={setShowChatMenu}
      showChatMenu={showChatMenu}
      handleAssignConversation={handleAssignConversation}
      handleTransferConversation={handleTransferConversation}
      handleSetLabels={handleSetLabels}
      handleToggleStar={handleToggleStar}
      handleNotes={handleNotes}
      setShowTemplateModal={setShowTemplateModal}
      loadApprovedTemplates={loadApprovedTemplates}
      setShowQaModal={setShowQaModal}
      renderMessageBody={renderMessageBody}
      openMessageMedia={openMessageMedia}
      inputRef={inputRef}
      msgEnd={msgEnd}
      showEmojiPicker={showEmojiPicker}
      EMOJI_SET={EMOJI_SET}
      setInput={setInput}
      ChatComposerNode={
        <ChatComposer
          C={C}
          I={I}
          input={input}
          onInputTyping={onInputTyping}
          onSend={send}
          onMic={handleMicInput}
          busySend={busy.send}
          onEmoji={handleEmoji}
          onAttach={handleAttachClick}
          inputRef={inputRef}
          fileInputRef={fileInputRef}
          onAttachmentSelected={handleAttachmentSelected}
          setShowEmojiPicker={setShowEmojiPicker}
        />
      }
      sharedDialogsNode={<SharedDialogs {...sharedDialogsProps} />}
    />
  );
  /* ── INBOX LIST VIEW ── */
  return (
    <div style={{ height:"100vh",display:"flex",flexDirection:"column",fontFamily:"'Segoe UI',system-ui,sans-serif",background:"#F8FAFC" }}>
      {/* header */}
      <div style={{
        background:`radial-gradient(620px 220px at 50% -40%, rgba(255,255,255,0.2), transparent 60%), linear-gradient(135deg,${C.orange} 0%,${C.orangeLight} 100%)`,
        padding:"10px 16px 14px",
        paddingTop:"calc(10px + env(safe-area-inset-top,0px))",
        flexShrink:0,
        boxShadow:"0 2px 16px rgba(249,115,22,0.35)",
        position:"relative",
      }}>
        <div style={{ display:"flex",alignItems:"center",gap:10,marginBottom:12 }}>
          <button onClick={()=>setView("profile")} style={{ background:"none",border:"none",padding:0,cursor:"pointer",flexShrink:0 }}>
            <Avatar name={uname} color="rgba(255,255,255,0.3)" size={38} online/>
          </button>
          <div style={{ flex:1 }}>
            <div style={{ color:"#fff",fontWeight:800,fontSize:18 }}>Textzy</div>
            <div style={{ color:"rgba(255,255,255,0.75)",fontSize:12 }}>{projectBadge} {project?.name}</div>
          </div>
          <button style={{ background:"rgba(255,255,255,0.18)",border:"none",color:"#fff",padding:"8px",borderRadius:10,cursor:"pointer",display:"flex",backdropFilter:"blur(4px)" }}>
            <I.Search/>
          </button>
          <button onClick={()=>setShowMainMenu(v=>!v)} style={{ background:"rgba(255,255,255,0.18)",border:"none",color:"#fff",padding:"8px",borderRadius:10,cursor:"pointer",display:"flex",backdropFilter:"blur(4px)" }}>
            <I.More/>
          </button>
                </div>
        {showMainMenu && (
          <div style={{ position:"absolute", right:16, top:"calc(10px + env(safe-area-inset-top,0px) + 50px)", zIndex:6, background:"#fff", borderRadius:12, boxShadow:"0 8px 24px rgba(0,0,0,0.18)", overflow:"hidden", minWidth:170 }}>
            {[
              { label: "Switch Project", onClick: openProjectSwitch },
              { label: "Settings", onClick: ()=>{ setShowMainMenu(false); setView("profile"); } },
              { label: "Notifications", onClick: ()=>{ setShowMainMenu(false); setShowNotificationsModal(true); } },
            ].map(item => (
              <button key={item.label} onClick={item.onClick} style={{ display:"block", width:"100%", textAlign:"left", border:"none", background:"#fff", padding:"11px 14px", fontSize:14, cursor:"pointer" }}>{item.label}</button>
            ))}
          </div>
        )}

        {/* search bar */}
        <div style={{ display:"flex",alignItems:"center",gap:8,background:"rgba(255,255,255,0.26)",borderRadius:14,padding:"10px 14px",backdropFilter:"blur(6px)", border:"1px solid rgba(255,255,255,0.22)" }}>
          <span style={{ color:"rgba(255,255,255,0.8)",display:"flex",flexShrink:0 }}><I.Search/></span>
          <input value={search} onChange={e=>setSearch(e.target.value)} placeholder="Search conversations..."
            style={{ border:"none",outline:"none",flex:1,fontSize:14,color:"#fff",background:"transparent",fontFamily:"inherit" }}
          />
          {search&&<button onClick={()=>setSearch("")} style={{ background:"none",border:"none",cursor:"pointer",color:"rgba(255,255,255,0.7)",padding:0,display:"flex" }}><I.Close/></button>}
        </div>
      </div>

      {/* tabs */}
      <div style={{ display:"flex",background:"#fff",borderBottom:`1px solid ${C.divider}`,flexShrink:0, padding:"4px 6px", gap:6 }}>
        {["All","Unread","Assigned","Starred"].map(t=>(
          <button key={t} onClick={()=>setTab(t)} style={{
            flex:1,padding:"10px 4px",border:"none",
            fontWeight:tab===t?700:500, color:tab===t?C.orange:C.textSub,
            fontSize:14,cursor:"pointer",fontFamily:"inherit",
            background:tab===t?`${C.orange}14`:"transparent",
            borderRadius:10,
            transition:"all 0.15s",
          }}>
            {t}
            {t==="Unread"&&unreadCount>0&&(
              <span style={{ marginLeft:5,background:C.unread,color:"#fff",borderRadius:20,padding:"0 6px",fontSize:11,fontWeight:700 }}>
                {unreadCount}
              </span>
            )}
          </button>
        ))}
      </div>

      {/* conversation list */}
      <div style={{ flex:1,overflowY:"auto", padding:"8px 10px 84px" }}>
        {filtered.length===0 ? (
          <div style={{ textAlign:"center",padding:"60px 20px",color:C.textMuted, background:"#fff", borderRadius:18, border:`1px dashed ${C.divider}` }}>
            <div style={{ fontSize:44,marginBottom:10 }}>💬</div>
            <div style={{ fontSize:15,fontWeight:500 }}>No conversations found</div>
          </div>
        ) : filtered.map((c,i)=>(
          <div key={c.id} onClick={()=>openChat(c.id)} style={{
            display:"flex",alignItems:"center",gap:13,padding:"13px 14px",
            cursor:"pointer",border:`1px solid ${C.divider}`,
            borderRadius:14,
            background:"#fff",transition:"background 0.1s, box-shadow 0.15s, transform 0.15s",
            animation:`fadeUp 0.2s ease-out ${i*0.04}s both`,
            marginBottom:8,
            boxShadow:"0 2px 8px rgba(15,23,42,0.04)",
          }}
            onMouseEnter={e=>{e.currentTarget.style.background=C.orangePale; e.currentTarget.style.boxShadow="0 6px 14px rgba(249,115,22,0.12)"; e.currentTarget.style.transform="translateY(-1px)";}}
            onMouseLeave={e=>{e.currentTarget.style.background="#fff"; e.currentTarget.style.boxShadow="0 2px 8px rgba(15,23,42,0.04)"; e.currentTarget.style.transform="translateY(0)";}}
          >
            <Avatar name={c.name} color={c.color} size={52} online={c.online}/>
            <div style={{ flex:1,minWidth:0 }}>
              <div style={{ display:"flex",justifyContent:"space-between",marginBottom:4,alignItems:"baseline" }}>
                <span style={{ fontWeight:700,fontSize:15,color:C.textMain,whiteSpace:"nowrap",overflow:"hidden",textOverflow:"ellipsis",maxWidth:"65%" }}>{c.name}</span>
                <span style={{ fontSize:11,color:c.unread>0?C.orange:C.textMuted,flexShrink:0,fontWeight:c.unread>0?600:400 }}>{c.time}</span>
              </div>
              <div style={{ display:"flex",justifyContent:"space-between",alignItems:"center" }}>
                <span style={{ fontSize:13,color:C.textSub,whiteSpace:"nowrap",overflow:"hidden",textOverflow:"ellipsis",maxWidth:"82%" }}>
                  {c.typing?<em style={{color:C.orange,fontStyle:"normal",fontWeight:500}}>typing...</em>:c.lastMsg}
                </span>
                {c.unread>0&&(
                  <span style={{ background:C.unread,color:"#fff",borderRadius:20,padding:"2px 8px",fontSize:11,fontWeight:700,flexShrink:0,boxShadow:`0 2px 6px ${C.orange}44` }}>
                    {c.unread}
                  </span>
                )}
              </div>
            </div>
          </div>
        ))}
      </div>

      {/* fab */}
      <button onClick={handleNewChat} style={{
        position:"fixed",bottom:28,right:20,
        width:60,height:60,borderRadius:"50%",border:"none",
        background:`linear-gradient(135deg,${C.orange},${C.orangeLight})`,
        color:"#fff",cursor:"pointer",
        display:"flex",alignItems:"center",justifyContent:"center",
        boxShadow:`0 10px 30px ${C.orange}66, 0 0 0 7px rgba(249,115,22,0.14)`,
        fontSize:26,fontWeight:300,
      }}><I.Plus/></button>

      <style>{`
        *{box-sizing:border-box;margin:0;padding:0;}
        ::-webkit-scrollbar{width:0;}
        @keyframes fadeUp{from{opacity:0;transform:translateY(6px)}to{opacity:1;transform:translateY(0)}}
        @keyframes tdot{0%,60%,100%{transform:translateY(0)}30%{transform:translateY(-5px)}}
        @keyframes pulse{0%,100%{opacity:1}50%{opacity:0.4}}
        @keyframes spin{to{transform:rotate(360deg)}}
      `}</style>
      <SharedDialogs {...sharedDialogsProps} />
    </div>
  );
}




















