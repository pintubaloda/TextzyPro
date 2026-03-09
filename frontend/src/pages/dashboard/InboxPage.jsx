import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import * as signalR from "@microsoft/signalr";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { ScrollArea } from "@/components/ui/scroll-area";
import { Textarea } from "@/components/ui/textarea";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
  DropdownMenuSeparator,
} from "@/components/ui/dropdown-menu";
import {
  Search,
  Filter,
  MoreVertical,
  Send,
  Paperclip,
  Smile,
  Phone,
  Video,
  Info,
  CheckCheck,
  Check,
  Clock,
  Star,
  Archive,
  Trash2,
  Tag,
  UserPlus,
  MessageCircle,
  Image,
  FileText,
  Mic,
  Bell,
  X,
  CornerUpLeft,
} from "lucide-react";
import { apiGet, apiGetBlob, apiPost, apiPostForm, buildIdempotencyKey, getAppBootstrap, getNotificationSettings, wabaGetOnboardingStatus } from "@/lib/api";
import { getSession } from "@/lib/api";
import { playNotificationTone, isNotificationAudioUnlocked, unlockNotificationAudio, wasNotificationEverEnabled, getNotificationVolume, setNotificationVolume, setNotificationSoundEnabled } from "@/lib/notificationAudio";
import { requestDesktopNotificationPermission, setRuntimePushConfig, showDesktopNotification, subscribeFcm, subscribePush } from "@/lib/browserNotifications";
import { toast } from "sonner";

const NOTIFICATION_STYLE_KEY = "textzy.inbox.notificationStyle";
const NOTIFY_LEADER_KEY = "textzy.inbox.notifyLeader";
const NOTIFY_LEADER_TTL_MS = 15000;
const DND_UNTIL_KEY = "textzy.inbox.dndUntilUtc";
const NOTIFY_PROMPT_DISMISSED_KEY = "textzy.inbox.notifyPromptDismissed";
const FULL_EMOJI_SET = [
  "😀","😁","😂","🤣","😃","😄","😅","😆","😉","😊","🙂","🙃","😍","🥰","😘","😗","😙","😚","😋","😛","😜","🤪","🤗","🤩","🤔",
  "😐","😶","🙄","😏","😣","😥","😮","🤐","😯","😪","😫","🥱","😴","😌","😛","🫡","🤝","👍","👎","👏","🙌","🙏","💪","🔥","✅",
  "❌","⚡","💯","🎉","✨","💬","📌","📎","🧩","📞","📹","🎤","📷","📝","🛒","💰","📦","🚚","📍","📅","⌛","⏱️","⚠️","❓","❤️"
];

const mediaKindLabel = (kind) => {
  const k = String(kind || "").toLowerCase();
  if (k === "image") return "Image";
  if (k === "video") return "Video";
  if (k === "audio") return "Audio";
  if (k === "document") return "Document";
  return "Attachment";
};

const messagePreviewText = (msg) => {
  if (!msg) return "Message";
  const body = String(msg.text || "").trim();
  if (msg.messageType?.startsWith("media:")) {
    const kind = String(msg.messageType.split(":")[1] || "media");
    const fileName = String(msg.media?.fileName || "").trim();
    const caption = String(msg.media?.caption || "").trim();
    if (caption) return `${mediaKindLabel(kind)}: ${caption}`;
    if (fileName) return `${mediaKindLabel(kind)}: ${fileName}`;
    return mediaKindLabel(kind);
  }
  return body || "Message";
};

const parseInteractiveButtonsFromType = (messageType) => {
  const raw = String(messageType || "");
  if (!raw.startsWith("interactive:")) return [];
  const parts = raw.split(":");
  if (parts.length < 3) return [];
  const encoded = parts.slice(2).join(":");
  return encoded
    .split("~")
    .map((x) => x.trim())
    .filter(Boolean)
    .slice(0, 3);
};

const parseInboundStructured = (text, messageType) => {
  const raw = String(text || "").trim();
  const type = String(messageType || "").toLowerCase();
  if (!raw) return { kind: "", data: null };

  if (raw.startsWith("Location:")) {
    const m = raw.match(/^Location:\s*(.*?)\s*\(([-\d.]+),([-\d.]+)\)\s*$/);
    if (m) return { kind: "location", data: { label: m[1]?.trim() || "Shared location", lat: m[2], lng: m[3] } };
    return { kind: "location", data: { label: raw.replace(/^Location:\s*/i, "") } };
  }

  if (raw.startsWith("Order:")) {
    const catalog = (raw.match(/catalog=([^;]+)/i)?.[1] || "").trim();
    const items = (raw.match(/items=([^;]+)/i)?.[1] || "").trim();
    const note = (raw.match(/text=(.+)$/i)?.[1] || "").trim();
    return { kind: "order", data: { catalog, items, note } };
  }

  if (raw.startsWith("Shared contacts:")) {
    const count = Number(raw.replace(/[^0-9]/g, "") || 0);
    return { kind: "contacts", data: { count } };
  }

  if (raw.startsWith("Reaction:")) {
    return { kind: "reaction", data: { emoji: raw.replace(/^Reaction:\s*/i, "").trim() } };
  }

  if (raw.startsWith("Unsupported message:") || raw === "Unsupported incoming WhatsApp message type." || raw === "Inbound unsupported message" || type === "unsupported") {
    return { kind: "unsupported", data: { reason: raw } };
  }

  if (raw.startsWith("Referral:")) {
    return { kind: "referral", data: { headline: raw.replace(/^Referral:\s*/i, "").trim() } };
  }

  return { kind: "", data: null };
};

const renderMessageText = (msg, customerName = "Customer", agentName = "Agent") => {
  const text = String(msg?.text || "");
  if (msg?.specialKind === "location") {
    const label = msg?.specialData?.label || "Shared location";
    const lat = msg?.specialData?.lat;
    const lng = msg?.specialData?.lng;
    const mapHref = lat && lng ? `https://maps.google.com/?q=${encodeURIComponent(`${lat},${lng}`)}` : "";
    return (
      <div className="space-y-1.5">
        <div className="text-xs font-semibold text-sky-700">Location</div>
        <p className="text-sm whitespace-pre-wrap break-words">{label}</p>
        {mapHref ? <a href={mapHref} target="_blank" rel="noreferrer" className="text-xs font-medium text-orange-600 underline">Open in Maps</a> : null}
      </div>
    );
  }
  if (msg?.specialKind === "order") {
    return (
      <div className="space-y-1.5">
        <div className="text-xs font-semibold text-indigo-700">Order Details</div>
        <p className="text-sm">Catalog: {msg?.specialData?.catalog || "-"}</p>
        <p className="text-sm">Items: {msg?.specialData?.items || "-"}</p>
        {msg?.specialData?.note ? <p className="text-sm whitespace-pre-wrap break-words">{msg.specialData.note}</p> : null}
      </div>
    );
  }
  if (msg?.specialKind === "contacts") {
    return (
      <div className="space-y-1">
        <div className="text-xs font-semibold text-emerald-700">Contacts Shared</div>
        <p className="text-sm">{msg?.specialData?.count || 0} contact(s)</p>
      </div>
    );
  }
  if (msg?.specialKind === "reaction") {
    return (
      <div className="space-y-1">
        <div className="text-xs font-semibold text-purple-700">Reaction</div>
        <p className="text-lg leading-none">{msg?.specialData?.emoji || "??"}</p>
      </div>
    );
  }
  if (msg?.specialKind === "unsupported") {
    return (
      <div className="space-y-1">
        <div className="text-xs font-semibold text-amber-700">Unsupported Type</div>
        <p className="text-sm whitespace-pre-wrap break-words">{msg?.specialData?.reason || text}</p>
      </div>
    );
  }

  const replyMatch = text.match(/^(?:↪\s*)?Reply to \(([^)]+)\):\s*([\s\S]*)$/);
  if (!replyMatch) {
    return <p className="text-sm whitespace-pre-wrap break-words">{text}</p>;
  }
  const [, rawReplyMeta, replyBodyRaw] = replyMatch;
  const replyMeta = String(rawReplyMeta || "")
    .replace(/^Customer\b/i, customerName || "Customer")
    .replace(/^You\b/i, agentName || "Agent")
    .replace(/^Agent\b/i, agentName || "Agent");
  const replyBody = String(replyBodyRaw || "").trimStart();
  return (
    <div className="space-y-1">
      <div className="inline-flex items-center gap-1.5 text-[13px] text-emerald-700 font-medium">
        <CornerUpLeft className="w-3.5 h-3.5" />
        <span>Reply to ({replyMeta})</span>
      </div>
      <p className="text-sm whitespace-pre-wrap break-words">{replyBody}</p>
    </div>
  );
};
const InboundMediaPreview = ({ msg, onOpen }) => {
  const [previewUrl, setPreviewUrl] = useState("");
  const [loading, setLoading] = useState(false);
  const mediaId = msg?.media?.mediaId;
  const mediaType = String(msg?.messageType || "");
  const kind = mediaType.startsWith("media:") ? mediaType.split(":")[1] : "";
  const fileName = String(msg?.media?.fileName || "").trim();
  const caption = String(msg?.media?.caption || "").trim();
  const mimeType = String(msg?.media?.mimeType || "").toLowerCase();
  const canInlineImage = kind === "image" || mimeType.startsWith("image/");

  useEffect(() => {
    let disposed = false;
    let objectUrl = "";
    if (!mediaId || !canInlineImage) {
      setPreviewUrl("");
      return () => {};
    }
    setLoading(true);
    apiGetBlob(`/api/messages/media/${encodeURIComponent(mediaId)}`)
      .then((blob) => {
        if (disposed) return;
        objectUrl = URL.createObjectURL(blob);
        setPreviewUrl(objectUrl);
      })
      .catch(() => {
        if (!disposed) setPreviewUrl("");
      })
      .finally(() => {
        if (!disposed) setLoading(false);
      });
    return () => {
      disposed = true;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [mediaId, canInlineImage]);

  if (!mediaId) return null;

  if (canInlineImage) {
    return (
      <div className="mt-2">
        {previewUrl ? (
          <button type="button" onClick={onOpen} className="block rounded-lg overflow-hidden border border-slate-200">
            <img src={previewUrl} alt={fileName || "image"} className="max-h-56 w-auto object-cover" />
          </button>
        ) : (
          <div className="text-xs text-slate-500">{loading ? "Loading image..." : "Image preview unavailable"}</div>
        )}
        <div className="mt-1.5 flex items-center gap-2">
          {fileName ? <span className="text-xs text-slate-600 truncate max-w-[230px]">{fileName}</span> : null}
          <Button type="button" size="sm" variant="outline" className="h-7 text-xs" onClick={onOpen}>Open</Button>
        </div>
        {caption ? <div className="text-xs text-slate-700 mt-1">{caption}</div> : null}
      </div>
    );
  }

  return (
    <div className="mt-2 rounded-lg border border-slate-200 bg-white px-3 py-2">
      <div className="flex items-center gap-2 text-sm text-slate-800">
        <Paperclip className="w-4 h-4 text-slate-500" />
        <span className="font-medium">{mediaKindLabel(kind)}</span>
      </div>
      <div className="text-xs text-slate-600 mt-1 truncate">{fileName || `${mediaKindLabel(kind)} attachment`}</div>
      {caption ? <div className="text-xs text-slate-700 mt-1">{caption}</div> : null}
      <Button type="button" size="sm" variant="outline" className="h-7 text-xs mt-2" onClick={onOpen}>Open attachment</Button>
    </div>
  );
};

const InboxPage = () => {
  const [selectedConversationId, setSelectedConversationId] = useState(null);
  const [message, setMessage] = useState("");
  const [searchQuery, setSearchQuery] = useState("");
  const [activeFilter, setActiveFilter] = useState("all");
  const [conversations, setConversations] = useState([]);
  const [messages, setMessages] = useState([]);
  const [contacts, setContacts] = useState([]);
  const [teamMembers, setTeamMembers] = useState([]);
  const [wabaDetails, setWabaDetails] = useState(null);
  const [newLabel, setNewLabel] = useState("");
  const [me, setMe] = useState(null);
  const [templates, setTemplates] = useState([]);
  const [selectedTemplateId, setSelectedTemplateId] = useState("");
  const [templateVars, setTemplateVars] = useState({});
  const [templatePresetTokens, setTemplatePresetTokens] = useState([]);
  const [templatePresetSuggested, setTemplatePresetSuggested] = useState({});
  const [templatePresetBusy, setTemplatePresetBusy] = useState(false);
  const [notes, setNotes] = useState([]);
  const [newNote, setNewNote] = useState("");
  const [faqs, setFaqs] = useState([]);
  const [sla, setSla] = useState({ breachedCount: 0, items: [] });
  const [typingUsers, setTypingUsers] = useState([]);
  const [replyToMessage, setReplyToMessage] = useState(null);
  const [showEmojiTray, setShowEmojiTray] = useState(false);
  const [showTemplateAttach, setShowTemplateAttach] = useState(false);
  const [showFaqAttach, setShowFaqAttach] = useState(false);
  const [showDetailsDrawer, setShowDetailsDrawer] = useState(false);
  const [emojiSearch, setEmojiSearch] = useState("");
  const [sendBusy, setSendBusy] = useState(false);
  const [assignBusy, setAssignBusy] = useState(false);
  const [transferBusy, setTransferBusy] = useState(false);
  const [starBusy, setStarBusy] = useState(false);
  const [voiceRecording, setVoiceRecording] = useState(false);
  const [pendingVoiceFile, setPendingVoiceFile] = useState(null);
  const [recordingSeconds, setRecordingSeconds] = useState(0);
  const [voiceMimeType, setVoiceMimeType] = useState("");
  const [audioUnlocked, setAudioUnlocked] = useState(() => isNotificationAudioUnlocked());
  const [dndUntilUtc, setDndUntilUtc] = useState(() => {
    try {
      return Number(localStorage.getItem(DND_UNTIL_KEY) || 0);
    } catch {
      return 0;
    }
  });
  const [notificationStyle, setNotificationStyle] = useState(() => {
    try {
      return localStorage.getItem(NOTIFICATION_STYLE_KEY) || "classic";
    } catch {
      return "classic";
    }
  });
  const [notificationVolume, setNotificationVolumeState] = useState(() => getNotificationVolume());
  const [desktopNotificationsEnabled, setDesktopNotificationsEnabled] = useState(true);
  const [dismissedSoundPrompt, setDismissedSoundPrompt] = useState(() => {
    try {
      return localStorage.getItem(NOTIFY_PROMPT_DISMISSED_KEY) === "1";
    } catch {
      return false;
    }
  });
  const selectedChatIdRef = useRef(null);
  const meEmailRef = useRef("");
  const playNotificationSoundRef = useRef(() => {});
  const typingTimerRef = useRef(null);
  const typingActiveRef = useRef(false);
  const fileInputRef = useRef(null);
  const imageInputRef = useRef(null);
  const docInputRef = useRef(null);
  const endMessageRef = useRef(null);
  const voiceRecorderRef = useRef(null);
  const voiceChunksRef = useRef([]);
  const voiceStreamRef = useRef(null);
  const recordingTimerRef = useRef(null);
  const tabIdRef = useRef(`tab_${Math.random().toString(36).slice(2)}_${Date.now()}`);
  const isNotifyLeaderRef = useRef(false);
  const leaderIntervalRef = useRef(null);
  const heartbeatIntervalRef = useRef(null);
  const broadcastRef = useRef(null);
  const seenRealtimeEventsRef = useRef(new Set());
  const signalRConnectionRef = useRef(null);
  const lastSoundAtRef = useRef(0);
  const faviconBlinkTimerRef = useRef(null);
  const faviconBlinkStateRef = useRef(false);
  const runtimePushRef = useRef({
    vapidPublicKey: "",
    firebaseConfig: null,
  });
  const loadConversationsRef = useRef(() => {});
  const loadThreadRef = useRef(() => {});
  const loadNotesRef = useRef(() => {});
  const loadSlaRef = useRef(() => {});
  const markRealtimeEventSeenRef = useRef(() => false);
  const emitCrossTabEventRef = useRef(() => {});
  const notifyDesktopRef = useRef(() => {});

  const realtimeSession = getSession();
  const realtimeTenantSlug = String(realtimeSession?.tenantSlug || "").trim();

  const mapConversation = (x) => {
    const id = x.id ?? x.Id ?? "";
    const customerName = x.customerName ?? x.CustomerName ?? "";
    const customerPhone = x.customerPhone ?? x.CustomerPhone ?? "";
    const status = x.status ?? x.Status ?? "";
    const lastMessageAtUtc = x.lastMessageAtUtc ?? x.LastMessageAtUtc ?? null;
    const createdAtUtc = x.createdAtUtc ?? x.CreatedAtUtc ?? null;
    const assignedUserId = x.assignedUserId ?? x.AssignedUserId ?? "";
    const assignedUserName = x.assignedUserName ?? x.AssignedUserName ?? "";
    const labelsCsv = x.labelsCsv ?? x.LabelsCsv ?? "";
    const canReply = x.canReply ?? x.CanReply ?? false;
    const hoursSinceInbound = x.hoursSinceInbound ?? x.HoursSinceInbound ?? 999;
    return {
    id,
    name: customerName || customerPhone || "Conversation",
    phone: customerPhone,
    lastMessage: status || "Conversation",
    time: lastMessageAtUtc || createdAtUtc || null,
    unread: Number(x.unreadCount ?? x.UnreadCount ?? 0),
    starred: false,
    channel: "whatsapp",
    avatar: (customerName || customerPhone || "U").slice(0, 2).toUpperCase(),
    assignedUserId,
    assignedUserName,
    labels: String(labelsCsv).split(",").map((z) => z.trim()).filter(Boolean),
    canReply: !!canReply,
    hoursSinceInbound: Number(hoursSinceInbound),
  };
  };
  const mapMessage = (x) => {
    const rawStatus = String(x.status ?? x.Status ?? "").toLowerCase();
    const sender = rawStatus === "received" ? "customer" : "agent";
    const normalizedStatus = sender === "agent" ? (rawStatus || "sent") : "received";
    const messageType = String(x.messageType ?? x.MessageType ?? "session");
    let text = x.body ?? x.Body ?? "";
    let media = null;
    if (messageType.startsWith("media:")) {
      const kind = messageType.split(":")[1] || "media";
      try {
        media = JSON.parse(String(x.body ?? x.Body ?? "{}"));
        text = `${kind === "audio" ? "🎤" : "📎"} ${kind.toUpperCase()}${media.caption ? ` - ${media.caption}` : ""}`;
      } catch {
        text = `📎 ${kind.toUpperCase()} attachment`;
      }
    } else if (messageType === "template") {
      const name = String(x.body ?? x.Body ?? "").split("|")[0] || "template";
      text = `🧩 Template: ${name}`;
    }
    const interactiveButtons = parseInteractiveButtonsFromType(messageType);
    const structured = parseInboundStructured(text, messageType);
    return {
    id: x.id ?? x.Id ?? crypto.randomUUID(),
    sender,
    text,
    time: (x.createdAtUtc ?? x.CreatedAtUtc) ? new Date(x.createdAtUtc ?? x.CreatedAtUtc).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" }) : "now",
    retryCount: Number(x.retryCount ?? x.RetryCount ?? 0),
    nextRetryAtUtc: x.nextRetryAtUtc ?? x.NextRetryAtUtc ?? null,
    lastError: x.lastError ?? x.LastError ?? "",
    queueProvider: x.queueProvider ?? x.QueueProvider ?? "memory",
    status: normalizedStatus,
    messageType,
    media,
    interactiveButtons,
    specialKind: structured.kind,
    specialData: structured.data,
  };
  };

  const openInboundMedia = async (msg) => {
    const mediaId = msg?.media?.mediaId;
    if (!mediaId) {
      toast.error("Media id not available");
      return;
    }
    try {
      const blob = await apiGetBlob(`/api/messages/media/${encodeURIComponent(mediaId)}`);
      const url = URL.createObjectURL(blob);
      const fileName = msg?.media?.fileName || `media-${mediaId}`;
      const link = document.createElement("a");
      link.href = url;
      link.download = fileName;
      link.target = "_blank";
      link.rel = "noopener noreferrer";
      document.body.appendChild(link);
      link.click();
      link.remove();
      setTimeout(() => URL.revokeObjectURL(url), 60_000);
    } catch (e) {
      toast.error(e?.message || "Failed to open media");
    }
  };

  const loadConversations = useCallback(
    () => apiGet("/api/inbox/conversations?take=100").then((c) => setConversations((c || []).map(mapConversation))).catch(() => {}),
    []
  );
  const loadThread = useCallback(
    (conversationId) =>
      apiGet(`/api/inbox/conversations/${conversationId}/messages?take=80`).then((rows) => setMessages((rows || []).map(mapMessage))).catch(() => setMessages([])),
    []
  );
  const loadNotes = useCallback(
    (conversationId) =>
      apiGet(`/api/inbox/conversations/${conversationId}/notes?take=50`).then((rows) => setNotes(rows || [])).catch(() => setNotes([])),
    []
  );
  const loadSla = useCallback(
    () => apiGet("/api/inbox/sla?thresholdMinutes=15").then((x) => setSla(x || { breachedCount: 0, items: [] })).catch(() => {}),
    []
  );

  const playNotificationSound = useCallback((frequency = 880) => {
    try {
      const hidden = typeof document !== "undefined" ? document.hidden : false;
      // Always allow sound on the currently visible tab.
      // For hidden tabs, only leader tab should play to avoid duplicates.
      if (hidden && !isNotifyLeaderRef.current) return;
      if (dndUntilUtc && Date.now() < dndUntilUtc) return;
      const now = Date.now();
      if (now - lastSoundAtRef.current < 1200) return;
      lastSoundAtRef.current = now;
      if (notificationStyle === "off") return;
      playNotificationTone(notificationStyle, frequency);
    } catch {
      // Ignore audio failures (autoplay policy / unsupported browser)
    }
  }, [dndUntilUtc, notificationStyle]);

  const acquireNotifyLeadership = useCallback(() => {
    const now = Date.now();
    const own = { tabId: tabIdRef.current, expiresAt: now + NOTIFY_LEADER_TTL_MS };
    try {
      const raw = localStorage.getItem(NOTIFY_LEADER_KEY);
      const current = raw ? JSON.parse(raw) : null;
      const expired = !current || Number(current.expiresAt || 0) <= now;
      if (expired || current.tabId === tabIdRef.current) {
        localStorage.setItem(NOTIFY_LEADER_KEY, JSON.stringify(own));
        isNotifyLeaderRef.current = true;
        return;
      }
      isNotifyLeaderRef.current = false;
    } catch {
      isNotifyLeaderRef.current = true;
    }
  }, []);

  const emitCrossTabEvent = useCallback((kind, key) => {
    if (!broadcastRef.current || !key) return;
    try {
      broadcastRef.current.postMessage({ kind, key, tabId: tabIdRef.current, at: Date.now() });
    } catch {
      // ignore
    }
  }, []);

  const markRealtimeEventSeen = useCallback((key) => {
    if (!key) return false;
    const cache = seenRealtimeEventsRef.current;
    if (cache.has(key)) return true;
    cache.add(key);
    if (cache.size > 800) {
      const first = cache.values().next().value;
      if (first) cache.delete(first);
    }
    return false;
  }, []);

  const notifyDesktop = useCallback((title, body, tag) => {
    try {
      if (!desktopNotificationsEnabled) return;
      if (!isNotifyLeaderRef.current) return;
      if (typeof document !== "undefined" && !document.hidden) return;
      if (dndUntilUtc && Date.now() < dndUntilUtc) return;
      showDesktopNotification({
        title: title || "New message",
        body: body || "You have a new message",
        tag: tag || "textzy-inbox"
      }).catch(() => {});
    } catch {
      // ignore
    }
  }, [desktopNotificationsEnabled, dndUntilUtc]);

  useEffect(() => {
    loadConversationsRef.current = loadConversations;
    loadThreadRef.current = loadThread;
    loadNotesRef.current = loadNotes;
    loadSlaRef.current = loadSla;
    markRealtimeEventSeenRef.current = markRealtimeEventSeen;
    emitCrossTabEventRef.current = emitCrossTabEvent;
    notifyDesktopRef.current = notifyDesktop;
  }, [loadConversations, loadThread, loadNotes, loadSla, markRealtimeEventSeen, emitCrossTabEvent, notifyDesktop]);

  const updateTabBadge = useCallback((count) => {
    if (typeof document === "undefined") return;
    const baseTitle = "Textzy";
    document.title = count > 0 ? `(${count}) ${baseTitle}` : baseTitle;
    let favicon = document.querySelector("link[rel='icon']");
    if (!favicon) {
      favicon = document.createElement("link");
      favicon.setAttribute("rel", "icon");
      document.head.appendChild(favicon);
    }
    const unreadSvg = "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 64 64'%3E%3Crect width='64' height='64' rx='12' fill='%23ffffff'/%3E%3Ccircle cx='32' cy='32' r='20' fill='%23f97316'/%3E%3C/svg%3E";
    const hidden = typeof document !== "undefined" ? document.hidden : false;
    if (faviconBlinkTimerRef.current) {
      clearInterval(faviconBlinkTimerRef.current);
      faviconBlinkTimerRef.current = null;
    }
    faviconBlinkStateRef.current = false;
    if (count > 0 && hidden) {
      favicon.setAttribute("href", unreadSvg);
      faviconBlinkTimerRef.current = window.setInterval(() => {
        faviconBlinkStateRef.current = !faviconBlinkStateRef.current;
        favicon.setAttribute("href", faviconBlinkStateRef.current ? unreadSvg : "/favicon.ico");
      }, 900);
    } else {
      favicon.setAttribute("href", "/favicon.ico");
    }
  }, []);

  useEffect(() => {
    meEmailRef.current = String(me?.email || "").toLowerCase();
  }, [me?.email]);

  useEffect(() => {
    playNotificationSoundRef.current = playNotificationSound;
  }, [playNotificationSound]);

  useEffect(() => {
    const unread = conversations.reduce((sum, c) => sum + Number(c.unread || 0), 0);
    updateTabBadge(unread);
  }, [conversations, updateTabBadge]);

  useEffect(() => () => {
    if (faviconBlinkTimerRef.current) {
      clearInterval(faviconBlinkTimerRef.current);
      faviconBlinkTimerRef.current = null;
    }
  }, []);

  useEffect(() => {
    try {
      localStorage.setItem(NOTIFICATION_STYLE_KEY, notificationStyle);
    } catch {
      // ignore storage issues
    }
  }, [notificationStyle]);

  useEffect(() => {
    setNotificationVolume(notificationVolume);
  }, [notificationVolume]);

  useEffect(() => {
    try {
      localStorage.setItem(NOTIFY_PROMPT_DISMISSED_KEY, dismissedSoundPrompt ? "1" : "0");
    } catch {
      // ignore
    }
  }, [dismissedSoundPrompt]);

  useEffect(() => {
    try {
      localStorage.setItem(DND_UNTIL_KEY, String(dndUntilUtc || 0));
    } catch {
      // ignore
    }
  }, [dndUntilUtc]);

  const loadRuntimePushConfig = useCallback(async () => {
    try {
      const bootstrap = await getAppBootstrap();
      const app = bootstrap?.app || {};
      const next = {
        vapidPublicKey: String(
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
      runtimePushRef.current = next;
      setRuntimePushConfig(next);
      return next;
    } catch {
      const fallback = {
        vapidPublicKey: String(
          process.env.REACT_APP_WEB_PUSH_PUBLIC_KEY ||
          process.env.VITE_WEB_PUSH_PUBLIC_KEY ||
          runtimePushRef.current?.vapidPublicKey ||
          ""
        ).trim(),
        firebaseConfig: runtimePushRef.current?.firebaseConfig || null,
      };
      runtimePushRef.current = fallback;
      setRuntimePushConfig(fallback);
      return fallback;
    }
  }, []);

  const ensureBrowserPushSubscription = useCallback(async (override = null) => {
    const perm = typeof Notification !== "undefined" ? Notification.permission : "denied";
    if (perm !== "granted") return;
    const runtimeCfg = override || runtimePushRef.current || {};
    const vapid = String(
      runtimeCfg.vapidPublicKey ||
      process.env.REACT_APP_WEB_PUSH_PUBLIC_KEY ||
      process.env.VITE_WEB_PUSH_PUBLIC_KEY ||
      ""
    ).trim();
    if (!vapid) return;

    const fcmToken = await subscribeFcm(vapid, runtimeCfg.firebaseConfig || null);
    if (fcmToken) {
      await apiPost("/api/notifications/subscriptions", {
        provider: "fcm",
        endpoint: fcmToken,
        p256dh: "",
        auth: "",
        userAgent: typeof navigator !== "undefined" ? navigator.userAgent : ""
      }).catch(() => {});
      return;
    }

    const sub = await subscribePush(vapid).catch(() => null);
    if (!sub?.endpoint) return;
    const keys = sub.keys || {};
    await apiPost("/api/notifications/subscriptions", {
      provider: "webpush",
      endpoint: sub.endpoint,
      p256dh: keys.p256dh || "",
      auth: keys.auth || "",
      userAgent: typeof navigator !== "undefined" ? navigator.userAgent : ""
    }).catch(() => {});
  }, []);

  useEffect(() => {
    loadRuntimePushConfig()
      .then((cfg) => ensureBrowserPushSubscription(cfg))
      .catch(() => {});
  }, [loadRuntimePushConfig, ensureBrowserPushSubscription]);

  useEffect(() => {
    if (!wasNotificationEverEnabled() || audioUnlocked) return;
    const tryUnlock = async () => {
      const ok = await unlockNotificationAudio();
      if (ok) setAudioUnlocked(true);
    };
    const handler = () => { tryUnlock(); };
    window.addEventListener("pointerdown", handler, { once: true });
    return () => window.removeEventListener("pointerdown", handler);
  }, [audioUnlocked]);

  const loadApprovedTemplates = useCallback(async () => {
    const tpl = await apiGet("/api/templates").catch(() => []);
    const approved = (tpl || []).filter((x) => String(x.status || "").toLowerCase() === "approved" && Number(x.channel) === 2);
    setTemplates(approved);
    if (approved.length > 0) setSelectedTemplateId((prev) => prev || String(approved[0].id));
  }, []);

  useEffect(() => {
    Promise.all([
      apiGet("/api/inbox/conversations?take=100"),
      apiGet("/api/contacts"),
      apiGet("/api/auth/team-members").catch(() => []),
      apiGet("/api/auth/me").catch(() => null),
      apiGet("/api/automation/faq").catch(() => []),
      wabaGetOnboardingStatus({ force: true }).catch(() => null),
      getNotificationSettings().catch(() => null),
    ])
      .then(([c, ct, tm, meData, faqRows, waba, notifyCfg]) => {
        const mapped = (c || []).map(mapConversation);
        setConversations(mapped);
        setSelectedConversationId((prev) => prev || mapped[0]?.id || null);
        setMessages([]);
        setContacts(ct || []);
        setTeamMembers(tm || []);
        setMe(meData);
        setFaqs((faqRows || []).filter((f) => f.isActive !== false));
        setWabaDetails(waba);
        if (notifyCfg) {
          if (notifyCfg.soundStyle) setNotificationStyle(String(notifyCfg.soundStyle));
          if (notifyCfg.soundVolume !== undefined && notifyCfg.soundVolume !== null) {
            const v = Number(notifyCfg.soundVolume);
            if (Number.isFinite(v)) setNotificationVolumeState(v);
          }
          setDesktopNotificationsEnabled(notifyCfg.desktopEnabled !== false);
          setNotificationSoundEnabled(notifyCfg.soundEnabled !== false);
          if (notifyCfg.dndUntilUtc) {
            const ts = Date.parse(String(notifyCfg.dndUntilUtc));
            if (Number.isFinite(ts)) setDndUntilUtc(ts);
          }
        }
        loadSla();
      })
      .catch(() => {
        setConversations([]);
        setMessages([]);
      });
  }, [loadSla]);

  useEffect(() => {
    if (!showTemplateAttach) return;
    if (templates.length > 0) return;
    loadApprovedTemplates().catch(() => {});
  }, [showTemplateAttach, templates.length, loadApprovedTemplates]);

  const formatAgo = (value) => {
    if (!value) return "just now";
    const dt = new Date(value);
    const sec = Math.max(1, Math.floor((Date.now() - dt.getTime()) / 1000));
    if (sec < 60) return `${sec}s ago`;
    const min = Math.floor(sec / 60);
    if (min < 60) return `${min}m ago`;
    const hr = Math.floor(min / 60);
    if (hr < 24) return `${hr}h ago`;
    const day = Math.floor(hr / 24);
    return `${day}d ago`;
  };

  const filteredConversations = useMemo(() => {
    return conversations.filter((c) => {
      const q = searchQuery.trim().toLowerCase();
      const matchesQ = !q || c.name.toLowerCase().includes(q) || (c.phone || "").toLowerCase().includes(q);
      if (!matchesQ) return false;
      if (activeFilter === "mine") return !!me && c.assignedUserId === me.userId;
      if (activeFilter === "unassigned") return !c.assignedUserId;
      return true;
    });
  }, [conversations, searchQuery, activeFilter, me]);

  const filterCounts = useMemo(
    () => ({
      all: conversations.length,
      mine: conversations.filter((c) => !!me && c.assignedUserId === me.userId).length,
      unassigned: conversations.filter((c) => !c.assignedUserId).length,
    }),
    [conversations, me]
  );

  useEffect(() => {
    if (filteredConversations.length === 0) {
      setSelectedConversationId(null);
      return;
    }
    const exists = filteredConversations.some((x) => x.id === selectedConversationId);
    if (!exists) setSelectedConversationId(filteredConversations[0].id);
  }, [filteredConversations, selectedConversationId]);

  const selectedChat = filteredConversations.find((x) => x.id === selectedConversationId) || filteredConversations[0] || {
    avatar: "NA",
    name: "No conversation",
    phone: "-",
    labels: [],
    canReply: false,
    assignedUserName: "",
  };
  const canReplyInSession = !!selectedChat.canReply;
  const isStarred = (selectedChat.labels || []).some((x) => String(x).toLowerCase() === "starred");
  const selectedContact = contacts.find((x) => x.phone === selectedChat.phone);
  const selectedTemplate = templates.find((x) => String(x.id) === selectedTemplateId) || templates[0];
  const agentDisplayName = useMemo(() => {
    const raw = String(me?.fullName || me?.name || me?.email || "Agent").trim();
    if (!raw) return "Agent";
    if (raw.includes("@")) return raw.split("@")[0];
    return raw;
  }, [me]);
  const getReplyTargetLabel = useCallback(
    (sender) => (sender === "agent" ? agentDisplayName : (selectedChat?.name || "Customer")),
    [agentDisplayName, selectedChat?.name]
  );
  const filteredEmojis = useMemo(() => {
    const q = emojiSearch.trim();
    if (!q) return FULL_EMOJI_SET;
    return FULL_EMOJI_SET.filter((e) => e.includes(q));
  }, [emojiSearch]);
  const templateParamIndexes = useMemo(() => {
    const body = selectedTemplate?.body || "";
    const matches = [...body.matchAll(/\{\{(\d+)\}\}/g)].map((m) => Number(m[1]));
    return Array.from(new Set(matches)).sort((a, b) => a - b);
  }, [selectedTemplate]);

  const tokenValueMap = useMemo(() => {
    const map = {};
    for (const t of templatePresetTokens || []) {
      if (!t?.key) continue;
      map[String(t.key).toLowerCase()] = t.value || "";
    }
    return map;
  }, [templatePresetTokens]);

  useEffect(() => {
    selectedChatIdRef.current = selectedChat?.id || null;
    if (!selectedChat?.id) {
      setMessages([]);
      setReplyToMessage(null);
      return;
    }
    setReplyToMessage(null);
    loadThread(selectedChat.id);
    loadNotes(selectedChat.id);
  }, [selectedChat?.id, loadNotes, loadThread]);

  useEffect(() => {
    if (!endMessageRef.current) return;
    endMessageRef.current.scrollIntoView({ behavior: "smooth", block: "end" });
  }, [messages, selectedChat?.id]);

  useEffect(() => {
    return () => {
      if (recordingTimerRef.current) clearInterval(recordingTimerRef.current);
      try {
        voiceStreamRef.current?.getTracks?.().forEach((t) => t.stop());
      } catch {
        // ignore
      }
    };
  }, []);

  useEffect(() => {
    const localTabId = tabIdRef.current;
    acquireNotifyLeadership();
    try {
      if (typeof BroadcastChannel !== "undefined") {
        const ch = new BroadcastChannel("textzy_inbox_notifications");
        ch.onmessage = (evt) => {
          const data = evt?.data || {};
          if (data?.type === "notify_settings_updated" && data?.payload) {
            const p = data.payload || {};
            setDesktopNotificationsEnabled(p.desktopEnabled !== false);
            setNotificationSoundEnabled(p.soundEnabled !== false);
            if (p.soundStyle) setNotificationStyle(String(p.soundStyle));
            const nextVol = Number(p.soundVolume ?? 1);
            if (Number.isFinite(nextVol)) {
              setNotificationVolumeState(nextVol);
              setNotificationVolume(nextVol);
            }
            setDndUntilUtc(p.dndUntilUtc || null);
            return;
          }
          const key = String(data?.key || "");
          if (!key) return;
          if (String(data?.tabId || "") === localTabId) return;
          markRealtimeEventSeen(key);
        };
        broadcastRef.current = ch;
      }
    } catch {
      // ignore
    }
    leaderIntervalRef.current = setInterval(acquireNotifyLeadership, 5000);
    return () => {
      if (leaderIntervalRef.current) clearInterval(leaderIntervalRef.current);
      try {
        if (isNotifyLeaderRef.current) {
          const raw = localStorage.getItem(NOTIFY_LEADER_KEY);
          const current = raw ? JSON.parse(raw) : null;
          if (current?.tabId === localTabId) localStorage.removeItem(NOTIFY_LEADER_KEY);
        }
      } catch {
        // ignore
      }
      try { broadcastRef.current?.close?.(); } catch {}
      broadcastRef.current = null;
    };
  }, [acquireNotifyLeadership, markRealtimeEventSeen]);

  const handleAttachFaq = (item) => {
    if (!item?.answer) return;
    setMessage((prev) => {
      const next = `${prev ? `${prev}\n` : ""}${item.answer}`;
      return next;
    });
    setShowFaqAttach(false);
  };

  const setReplyTarget = (msg) => {
    if (!msg) return;
    const preview = messagePreviewText(msg);
    setReplyToMessage({
      id: msg.id,
      sender: msg.sender,
      text: preview.length > 160 ? `${preview.slice(0, 160)}...` : preview,
      time: msg.time,
    });
    toast.success("Reply target selected");
  };

  const toggleTemplatePanel = () => {
    setShowTemplateAttach((prev) => {
      const next = !prev;
      if (next) setShowFaqAttach(false);
      return next;
    });
  };

  const toggleFaqPanel = () => {
    setShowFaqAttach((prev) => {
      const next = !prev;
      if (next) setShowTemplateAttach(false);
      return next;
    });
  };

  const isWhatsAppAudioMimeSupported = (mimeType) => {
    const m = String(mimeType || "").toLowerCase();
    return m.includes("ogg") || m.includes("mpeg") || m.includes("mp4") || m.includes("aac") || m.includes("wav") || m.includes("amr");
  };

  const pickRecorderMimeType = () => {
    if (typeof MediaRecorder === "undefined" || !MediaRecorder.isTypeSupported) return "";
    const candidates = [
      "audio/ogg;codecs=opus",
      "audio/ogg",
      "audio/mp4",
      "audio/mpeg",
      "audio/aac",
    ];
    return candidates.find((m) => MediaRecorder.isTypeSupported(m)) || "";
  };

  const requestBrowserNotificationAndSound = async () => {
    try {
      const perm = await requestDesktopNotificationPermission();
      if (perm === "granted") {
        const cfg = await loadRuntimePushConfig();
        await ensureBrowserPushSubscription(cfg);
      }
      const ok = await unlockNotificationAudio();
      setAudioUnlocked(ok || isNotificationAudioUnlocked());
      setDismissedSoundPrompt(true);
      if (ok) toast.success("Notification sounds enabled");
      else toast.error("Could not enable sound. Click once again and allow browser permissions.");
    } catch {
      toast.error("Unable to enable notification sounds");
    }
  };

  const shouldShowEnableSoundPrompt = !audioUnlocked && !dismissedSoundPrompt && !wasNotificationEverEnabled();

  const handleSendMessage = async () => {
    if (pendingVoiceFile) {
      const ok = await uploadAndSendMedia(pendingVoiceFile, "audio");
      if (ok) {
        setPendingVoiceFile(null);
        setVoiceMimeType("");
      }
      return;
    }
    if (!canReplyInSession) {
      toast.error("24-hour session expired. Send an approved template first.");
      return;
    }
    if (!message.trim() || sendBusy) return;
    try {
      setSendBusy(true);
      stopTyping();
      const replyTargetLabel = getReplyTargetLabel(replyToMessage?.sender);
      const replyPrefix = replyToMessage ? `↪ Reply to (${replyTargetLabel} ${replyToMessage.time}): ${replyToMessage.text}\n` : "";
      await apiPost("/api/messages/send", {
        recipient: selectedChat.phone || "+910000000000",
        body: `${replyPrefix}${message}`,
        channel: 2,
        idempotencyKey: buildIdempotencyKey("inbox"),
      });
      setMessage("");
      setReplyToMessage(null);
      await loadThread(selectedChat.id);
      await loadConversations();
    } catch (e) {
      toast.error(e?.message || "Message send failed");
    } finally {
      setSendBusy(false);
    }
  };

  const handleSendTemplateFallback = async () => {
    const tpl = selectedTemplate;
    if (!tpl || !selectedChat?.phone) {
      toast.error("No approved WhatsApp template available.");
      return;
    }
    try {
      stopTyping();
      const params = templateParamIndexes.map((idx) => (templateVars[idx] || "").trim());
      if (params.some((p) => !p)) {
        toast.error("Fill all template variables before sending.");
        return;
      }
      setSendBusy(true);
      const replyTargetLabel = getReplyTargetLabel(replyToMessage?.sender);
      const replyPrefix = replyToMessage ? `↪ Reply to (${replyTargetLabel} ${replyToMessage.time}): ${replyToMessage.text}\n` : "";
      await apiPost("/api/messages/send", {
        recipient: selectedChat.phone,
        body: `${replyPrefix}${tpl.body || "Template message"}`,
        channel: 2,
        useTemplate: true,
        templateName: tpl.name,
        templateLanguageCode: tpl.language || "en",
        templateParameters: params,
        idempotencyKey: buildIdempotencyKey("tpl"),
      });
      toast.success(`Template sent: ${tpl.name}`);
      setReplyToMessage(null);
      await loadThread(selectedChat.id);
      await loadConversations();
      setShowTemplateAttach(false);
    } catch (e) {
      toast.error(e?.message || "Template send failed");
    } finally {
      setSendBusy(false);
    }
  };

  useEffect(() => {
    setTemplateVars({});
  }, [selectedTemplateId]);

  const loadTemplatePresets = useCallback(async () => {
    if (!selectedTemplate?.id || !selectedChat?.phone) {
      setTemplatePresetTokens([]);
      setTemplatePresetSuggested({});
      return;
    }
    try {
      setTemplatePresetBusy(true);
      const query = encodeURIComponent(selectedChat.phone);
      const res = await apiGet(`/api/templates/${selectedTemplate.id}/presets?recipient=${query}`);
      const tokens = Array.isArray(res?.tokens) ? res.tokens : [];
      const suggested = res?.suggestedByIndex && typeof res.suggestedByIndex === "object" ? res.suggestedByIndex : {};
      setTemplatePresetTokens(tokens);
      setTemplatePresetSuggested(suggested);
    } catch {
      setTemplatePresetTokens([]);
      setTemplatePresetSuggested({});
    } finally {
      setTemplatePresetBusy(false);
    }
  }, [selectedTemplate?.id, selectedChat?.phone]);

  useEffect(() => {
    loadTemplatePresets();
  }, [loadTemplatePresets]);

  const applySuggestedTemplateVars = () => {
    if (!templatePresetSuggested || Object.keys(templatePresetSuggested).length === 0) {
      toast.error("No system presets available for this contact.");
      return;
    }
    setTemplateVars((prev) => {
      const next = { ...prev };
      for (const idx of templateParamIndexes) {
        const v = templatePresetSuggested[String(idx)];
        if (v) next[idx] = v;
      }
      return next;
    });
    toast.success("System variables auto-filled");
  };

  const previewTemplateBody = useMemo(() => {
    const body = selectedTemplate?.body || "";
    return body.replace(/\{\{(\d+)\}\}/g, (_, i) => {
      const raw = templateVars[Number(i)] || `{{${i}}}`;
      const marker = String(raw || "").trim();
      if (marker.startsWith("{{") && marker.endsWith("}}")) {
        const token = marker.slice(2, -2).trim().toLowerCase();
        return tokenValueMap[token] || marker;
      }
      return raw;
    });
  }, [selectedTemplate?.body, templateVars, tokenValueMap]);

  useEffect(() => {
    if (!realtimeTenantSlug) return;
    let disposed = false;
    let joined = false;
    let startPromise = null;
    const runtimeConfig = typeof window !== "undefined" ? (window.__APP_CONFIG__ || {}) : {};
    const baseUrl =
      runtimeConfig.API_BASE ||
      process.env.REACT_APP_API_BASE ||
      process.env.VITE_API_BASE ||
      "https://textzy-backend-production.up.railway.app";
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`${baseUrl}/hubs/inbox?tenantSlug=${encodeURIComponent(realtimeTenantSlug)}`, {
        withCredentials: true,
        transport: signalR.HttpTransportType.ServerSentEvents | signalR.HttpTransportType.LongPolling,
      })
      .withAutomaticReconnect()
      .build();
    signalRConnectionRef.current = connection;

    const joinRoom = () => connection.invoke("JoinTenantRoom", realtimeTenantSlug).catch(() => {});
    const activeConversationId = () => selectedChatIdRef.current;
    const markState = () => {
      const method = typeof document !== "undefined" && document.hidden ? "SetUserInactive" : "SetUserActive";
      connection.invoke(method, realtimeTenantSlug, activeConversationId()).catch(() => {});
    };

    const refreshMessageViews = () => {
      loadConversationsRef.current?.();
      const activeId = activeConversationId();
      if (activeId) loadThreadRef.current?.(activeId);
    };
    connection.on("message.queued", (evt) => {
      const key = `queued:${evt?.id || evt?.recipient || "x"}:${evt?.createdAtUtc || ""}`;
      if (markRealtimeEventSeenRef.current?.(key)) return;
      emitCrossTabEventRef.current?.("message.queued", key);
      refreshMessageViews();
    });
    connection.on("message.sent", (evt) => {
      const key = `sent:${evt?.id || evt?.recipient || "x"}:${evt?.createdAtUtc || ""}`;
      if (markRealtimeEventSeenRef.current?.(key)) return;
      emitCrossTabEventRef.current?.("message.sent", key);
      refreshMessageViews();
      playNotificationSoundRef.current?.(980);
    });
    connection.on("webhook.inbound", (evt) => {
      const key = `inbound:${evt?.phoneNumberId || "x"}:${evt?.inboundCount || 0}:${Date.now() / 1000 | 0}`;
      if (markRealtimeEventSeenRef.current?.(key)) return;
      emitCrossTabEventRef.current?.("webhook.inbound", key);
      loadConversationsRef.current?.();
      const activeId = activeConversationId();
      if (activeId) loadThreadRef.current?.(activeId);
      loadSlaRef.current?.();
      playNotificationSoundRef.current?.(760);
      notifyDesktopRef.current?.("New WhatsApp message", "You received a new customer message.", `inbound:${evt?.phoneNumberId || "tenant"}`);
    });
    connection.on("conversation.assigned", () => {
      loadConversationsRef.current?.();
      const activeId = activeConversationId();
      if (activeId) loadThreadRef.current?.(activeId);
    });
    connection.on("conversation.transferred", () => {
      loadConversationsRef.current?.();
      const activeId = activeConversationId();
      if (activeId) loadThreadRef.current?.(activeId);
    });
    connection.on("conversation.labels", () => loadConversationsRef.current?.());
    connection.on("conversation.note", () => {
      const activeId = activeConversationId();
      if (activeId) loadNotesRef.current?.(activeId);
    });
    connection.onreconnected(() => {
      joinRoom();
      markState();
      loadConversationsRef.current?.();
      const activeId = activeConversationId();
      if (activeId) loadThreadRef.current?.(activeId);
      loadSlaRef.current?.();
    });
    connection.on("conversation.typing", (e) => {
      const activeId = activeConversationId();
      if (!e?.conversationId || String(e.conversationId) !== String(activeId)) return;
      if (e.user && meEmailRef.current && String(e.user).toLowerCase() === meEmailRef.current) return;
      const typingLabel = (e.userName || e.user || "Agent").toString();
      setTypingUsers((prev) => {
        const next = new Set(prev);
        if (e.isTyping) next.add(typingLabel);
        else next.delete(typingLabel);
        return [...next];
      });
      if (e.isTyping) {
        setTimeout(() => {
          setTypingUsers((prev) => prev.filter((u) => u !== typingLabel));
        }, 3000);
      }
    });
    connection.onclose(() => {
      setTypingUsers([]);
    });

    startPromise = connection.start()
      .then(() => {
        if (disposed) return connection.stop().catch(() => {});
        return joinRoom().then(() => {
          joined = true;
          markState();
          if (heartbeatIntervalRef.current) clearInterval(heartbeatIntervalRef.current);
          heartbeatIntervalRef.current = setInterval(() => {
            connection.invoke("Heartbeat", realtimeTenantSlug, activeConversationId()).catch(() => {});
          }, 30000);
        });
      })
      .catch((err) => {
        if (disposed) return;
        // Keep inbox usable without noisy banner flashes during tenant/project switches.
        console.warn("Realtime connection start failed; falling back to action-based refresh.", err);
      });

    return () => {
      disposed = true;
      signalRConnectionRef.current = null;
      if (typingTimerRef.current) clearTimeout(typingTimerRef.current);
      if (heartbeatIntervalRef.current) clearInterval(heartbeatIntervalRef.current);
      if (typeof document !== "undefined") {
        document.removeEventListener("visibilitychange", markState);
      }
      if (joined) {
        connection.invoke("LeaveTenantRoom", realtimeTenantSlug).catch(() => {});
      }
      Promise.resolve(startPromise).finally(() => {
        if (connection.state === signalR.HubConnectionState.Connected || connection.state === signalR.HubConnectionState.Reconnecting) {
          connection.stop().catch(() => {});
        }
      });
    };
  }, [realtimeTenantSlug]);

  useEffect(() => {
    const conn = signalRConnectionRef.current;
    const s = getSession();
    if (!conn || !s?.tenantSlug) return;
    conn.invoke(typeof document !== "undefined" && document.hidden ? "SetUserInactive" : "SetUserActive", s.tenantSlug, selectedChatIdRef.current || "")
      .catch(() => {});
  }, [selectedConversationId]);

  useEffect(() => {
    const handler = () => {
      const conn = signalRConnectionRef.current;
      const s = getSession();
      if (!conn || !s?.tenantSlug) return;
      conn.invoke(document.hidden ? "SetUserInactive" : "SetUserActive", s.tenantSlug, selectedChatIdRef.current || "")
        .catch(() => {});
    };
    if (typeof document !== "undefined") {
      document.addEventListener("visibilitychange", handler);
    }
    return () => {
      if (typeof document !== "undefined") {
        document.removeEventListener("visibilitychange", handler);
      }
    };
  }, []);

  const emitTyping = useCallback((isTyping) => {
    if (!selectedChat?.id) return;
    apiPost("/api/inbox/typing", { conversationId: selectedChat.id, isTyping }).catch(() => {});
  }, [selectedChat?.id]);

  const handleInputTyping = useCallback((value) => {
    setMessage(value);
    if (!selectedChat?.id) return;
    if (!typingActiveRef.current) {
      typingActiveRef.current = true;
      emitTyping(true);
    }
    if (typingTimerRef.current) clearTimeout(typingTimerRef.current);
    typingTimerRef.current = setTimeout(() => {
      typingActiveRef.current = false;
      emitTyping(false);
    }, 1200);
  }, [emitTyping, selectedChat?.id]);

  const stopTyping = useCallback(() => {
    if (typingTimerRef.current) clearTimeout(typingTimerRef.current);
    if (typingActiveRef.current) {
      typingActiveRef.current = false;
      emitTyping(false);
    }
  }, [emitTyping]);

  const handleAssign = async (member) => {
    if (!selectedChat.id || assignBusy) return;
    try {
      setAssignBusy(true);
      const updated = await apiPost(`/api/inbox/conversations/${selectedChat.id}/assign`, {
        userId: String(member.id),
        userName: member.name,
      });
      setConversations((prev) =>
        prev.map((x) => (x.id === selectedChat.id ? { ...x, assignedUserId: updated.assignedUserId, assignedUserName: updated.assignedUserName } : x))
      );
      toast.success(`Assigned to ${member.name}`);
    } catch (e) {
      toast.error(e?.message || "Failed to assign");
    } finally {
      setAssignBusy(false);
    }
  };

  const handleTransfer = async (member) => {
    if (!selectedChat.id || transferBusy) return;
    try {
      setTransferBusy(true);
      const updated = await apiPost(`/api/inbox/conversations/${selectedChat.id}/transfer`, {
        userId: String(member.id),
        userName: member.name,
      });
      setConversations((prev) =>
        prev.map((x) => (x.id === selectedChat.id ? { ...x, assignedUserId: updated.assignedUserId, assignedUserName: updated.assignedUserName } : x))
      );
      toast.success(`Transferred to ${member.name}`);
    } catch (e) {
      toast.error(e?.message || "Failed to transfer chat");
    } finally {
      setTransferBusy(false);
    }
  };

  const handleAddLabel = async () => {
    const label = newLabel.trim();
    if (!label || !selectedChat.id) return;
    try {
      const labels = Array.from(new Set([...(selectedChat.labels || []), label]));
      const updated = await apiPost(`/api/inbox/conversations/${selectedChat.id}/labels`, { labels });
      const nextLabels = (updated.labelsCsv || "").split(",").map((z) => z.trim()).filter(Boolean);
      setConversations((prev) => prev.map((x) => (x.id === selectedChat.id ? { ...x, labels: nextLabels } : x)));
      setNewLabel("");
      toast.success("Label added");
    } catch {
      toast.error("Failed to add label");
    }
  };

  const handleToggleStar = async () => {
    if (!selectedChat?.id || starBusy) return;
    try {
      setStarBusy(true);
      const current = selectedChat.labels || [];
      const has = current.some((x) => String(x).toLowerCase() === "starred");
      const labels = has ? current.filter((x) => String(x).toLowerCase() !== "starred") : [...current, "starred"];
      const updated = await apiPost(`/api/inbox/conversations/${selectedChat.id}/labels`, { labels });
      const nextLabels = (updated.labelsCsv || "").split(",").map((z) => z.trim()).filter(Boolean);
      setConversations((prev) => prev.map((x) => (x.id === selectedChat.id ? { ...x, labels: nextLabels } : x)));
      toast.success(has ? "Star removed" : "Conversation starred");
    } catch (e) {
      toast.error(e?.message || "Failed to update star");
    } finally {
      setStarBusy(false);
    }
  };

  const handleCall = () => {
    const digits = String(selectedChat?.phone || "").replace(/[^\d]/g, "");
    if (!digits) return toast.error("Phone not available");
    window.location.href = `tel:+${digits}`;
  };

  const handleVideoCall = () => {
    const digits = String(selectedChat?.phone || "").replace(/[^\d]/g, "");
    if (!digits) return toast.error("Phone not available");
    window.open(`https://wa.me/${digits}`, "_blank", "noopener,noreferrer");
  };

  const uploadAndSendMedia = async (file, mediaType, caption = "") => {
    if (!file || !selectedChat?.phone) return;
    if (!canReplyInSession) {
      toast.error("24-hour session expired. Send an approved template first.");
      return false;
    }
    try {
      setSendBusy(true);
      const fd = new FormData();
      fd.append("recipient", selectedChat.phone);
      fd.append("file", file);
      fd.append("mediaType", mediaType);
      const replyTargetLabel = getReplyTargetLabel(replyToMessage?.sender);
      const replyPrefix = replyToMessage ? `↪ Reply to (${replyTargetLabel} ${replyToMessage.time}): ${replyToMessage.text}\n` : "";
      fd.append("caption", `${replyPrefix}${caption || ""}`.trim());
      const res = await apiPostForm("/api/messages/upload-whatsapp-media", fd, { "Idempotency-Key": buildIdempotencyKey(mediaType) });
      const statusText = String(res?.status || "Queued").toLowerCase();
      toast.success(`${mediaType[0].toUpperCase()}${mediaType.slice(1)} ${statusText === "queued" ? "queued" : "sent"}`);
      setReplyToMessage(null);
      await loadThread(selectedChat.id);
      await loadConversations();
      return true;
    } catch (e) {
      toast.error(e?.message || `${mediaType} send failed`);
      return false;
    } finally {
      setSendBusy(false);
    }
  };

  const handlePickAttachment = () => fileInputRef.current?.click();
  const handlePickImage = () => imageInputRef.current?.click();
  const handlePickDocument = () => docInputRef.current?.click();

  const handleAttachmentSelected = async (e, forcedType = null) => {
    const file = e.target.files?.[0];
    e.target.value = "";
    if (!file) return;
    const mime = String(file.type || "").toLowerCase();
    const mediaType = forcedType || (mime.startsWith("image/") ? "image" : mime.startsWith("audio/") ? "audio" : mime.startsWith("video/") ? "video" : "document");
    await uploadAndSendMedia(file, mediaType, mediaType === "audio" ? "" : (message || "").trim());
  };

  const handleInsertEmoji = (emoji) => {
    setMessage((prev) => `${prev}${emoji}`);
  };

  const handleVoiceNote = async () => {
    if (!selectedChat?.phone) return;
    if (!voiceRecording) {
      try {
        const mimeType = pickRecorderMimeType();
        if (!mimeType) {
          toast.error("Voice recording not supported for WhatsApp in this browser. Use Chrome/Edge or upload audio file.");
          return;
        }
        const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
        const recorder = new MediaRecorder(stream, { mimeType });
        voiceStreamRef.current = stream;
        voiceChunksRef.current = [];
        setRecordingSeconds(0);
        setVoiceMimeType(mimeType);
        if (recordingTimerRef.current) clearInterval(recordingTimerRef.current);
        recordingTimerRef.current = setInterval(() => setRecordingSeconds((s) => s + 1), 1000);
        recorder.ondataavailable = (evt) => {
          if (evt.data?.size > 0) voiceChunksRef.current.push(evt.data);
        };
        recorder.onstop = () => {
          if (recordingTimerRef.current) clearInterval(recordingTimerRef.current);
          const blob = new Blob(voiceChunksRef.current, { type: recorder.mimeType || mimeType || "audio/ogg" });
          if (!blob.size) {
            toast.error("No voice captured. Try again.");
            return;
          }
          if (!isWhatsAppAudioMimeSupported(blob.type || mimeType)) {
            setPendingVoiceFile(null);
            setVoiceMimeType("");
            toast.error("Recorded format is not WhatsApp-supported. Please upload .ogg/.mp3/.m4a audio file.");
            try {
              voiceStreamRef.current?.getTracks?.().forEach((t) => t.stop());
            } catch {
              // ignore
            }
            return;
          }
          const ext = blob.type.includes("ogg")
            ? "ogg"
            : blob.type.includes("mp4")
              ? "m4a"
              : blob.type.includes("mpeg")
                ? "mp3"
                : "webm";
          const file = new File([blob], `voice-${Date.now()}.${ext}`, { type: blob.type || mimeType || "audio/ogg" });
          setPendingVoiceFile(file);
          toast.success("Voice ready. Click Send to deliver.");
          try {
            voiceStreamRef.current?.getTracks?.().forEach((t) => t.stop());
          } catch {
            // ignore
          }
        };
        voiceRecorderRef.current = recorder;
        recorder.start();
        setPendingVoiceFile(null);
        setVoiceRecording(true);
        toast.success("Recording started.");
      } catch (e) {
        toast.error(e?.message || "Microphone access denied");
      }
      return;
    }
    try {
      voiceRecorderRef.current?.stop();
      setVoiceRecording(false);
      toast.success("Recording stopped.");
    } catch {
      setVoiceRecording(false);
      if (recordingTimerRef.current) clearInterval(recordingTimerRef.current);
      toast.error("Voice send failed");
    }
  };

  const handleAddNote = async () => {
    const body = newNote.trim();
    if (!body || !selectedChat?.id) return;
    try {
      await apiPost(`/api/inbox/conversations/${selectedChat.id}/notes`, { body });
      setNewNote("");
      loadNotes(selectedChat.id);
      toast.success("Note added");
    } catch {
      toast.error("Failed to add note");
    }
  };

  const getStatusIcon = (status) => {
    switch (status) {
      case "read":
        return <CheckCheck className="w-4 h-4 text-blue-500" />;
      case "delivered":
        return <CheckCheck className="w-4 h-4 text-slate-400" />;
      case "sent":
      case "acceptedbymeta":
      case "accepted":
        return <Check className="w-4 h-4 text-slate-400" />;
      case "queued":
      case "processing":
      case "retryscheduled":
        return <Clock className="w-4 h-4 text-amber-500" />;
      case "failed":
        return <Clock className="w-4 h-4 text-red-500" />;
      default:
        return <Clock className="w-4 h-4 text-slate-400" />;
    }
  };

  const detailsPanelContent = (
    <div className="p-6 min-h-full">
      <div className="text-center mb-6 rounded-2xl border border-slate-200 bg-slate-50 p-5">
        <Avatar className="w-24 h-24 mx-auto mb-4"><AvatarFallback className="bg-gradient-to-br from-orange-400 to-orange-600 text-white text-2xl font-medium">{selectedChat.avatar}</AvatarFallback></Avatar>
        <h3 className="font-semibold text-slate-900 text-base truncate">{selectedChat.name}</h3>
        <p className="text-slate-500 text-sm truncate">{selectedChat.phone}</p>
        <p className="text-xs text-slate-500 mt-1">Assigned: <span className="font-medium text-slate-700">{selectedChat.assignedUserName || "Unassigned"}</span></p>
      </div>

      <div className="space-y-4">
        <div className="p-5 rounded-xl border border-slate-200 bg-slate-50">
          <p className="text-sm font-medium text-slate-800 mb-2">Contact Info</p>
          <div className="space-y-2 text-sm">
            <div className="flex justify-between gap-2"><span className="text-slate-500">Name</span><span className="text-slate-900 text-right break-words">{selectedContact?.name || selectedChat.name || "-"}</span></div>
            <div className="flex justify-between gap-2"><span className="text-slate-500">Phone</span><span className="text-slate-900 text-right break-all">{selectedContact?.phone || selectedChat.phone || "-"}</span></div>
            <div className="flex justify-between gap-2"><span className="text-slate-500">Email</span><span className="text-slate-900 text-right break-all">{selectedContact?.email || "-"}</span></div>
            <div className="flex justify-between gap-2"><span className="text-slate-500">Added</span><span className="text-slate-900 text-right">{selectedContact?.createdAtUtc ? new Date(selectedContact.createdAtUtc).toLocaleDateString() : "-"}</span></div>
          </div>
        </div>

        <div className="p-5 rounded-xl border border-slate-200 bg-slate-50">
          <p className="text-sm font-medium text-slate-800 mb-2">Labels</p>
          <div className="flex flex-wrap gap-2">{(selectedChat.labels || []).length === 0 ? <span className="text-sm text-slate-500">No labels</span> : (selectedChat.labels || []).map((label) => <Badge key={label} variant="outline" className="bg-orange-50 text-orange-700 border-orange-200">{label}</Badge>)}</div>
          <div className="mt-3 flex gap-2">
            <Input placeholder="Add label" className="border-slate-200 bg-white text-slate-900 placeholder:text-slate-400" value={newLabel} onChange={(e) => setNewLabel(e.target.value)} onKeyDown={(e) => { if (e.key === "Enter") { e.preventDefault(); handleAddLabel(); } }} />
            <Button onClick={handleAddLabel} size="sm" className="bg-orange-500 hover:bg-orange-600 text-white rounded-xl px-5">Add</Button>
          </div>
        </div>

        <div className="p-5 rounded-xl border border-slate-200 bg-slate-50">
          <p className="text-sm font-medium text-slate-800 mb-2">WABA Profile</p>
          <div className="space-y-2">
            <div className="flex items-center justify-between text-sm"><span className="text-slate-500">Business</span><span className="text-slate-900 font-medium">{wabaDetails?.businessName || "-"}</span></div>
            <div className="flex items-center justify-between text-sm"><span className="text-slate-500">WABA ID</span><span className="text-slate-900 font-medium">{wabaDetails?.wabaId || "-"}</span></div>
            <div className="flex items-center justify-between text-sm"><span className="text-slate-500">Phone Number ID</span><span className="text-slate-900 font-medium">{wabaDetails?.phoneNumberId || "-"}</span></div>
            <div className="flex items-center justify-between text-sm"><span className="text-slate-500">Display Number</span><span className="text-slate-900 font-medium">{wabaDetails?.displayPhoneNumber || wabaDetails?.phone || "-"}</span></div>
            <div className="flex items-center justify-between text-sm"><span className="text-slate-500">Status</span><Badge className={(wabaDetails?.readyToSend || wabaDetails?.isConnected || String(wabaDetails?.state || "").toLowerCase() === "ready") ? "bg-green-100 text-green-700 hover:bg-green-100" : "bg-amber-100 text-amber-700 hover:bg-amber-100"}>{(wabaDetails?.readyToSend || wabaDetails?.isConnected || String(wabaDetails?.state || "").toLowerCase() === "ready") ? "Ready" : (wabaDetails?.state || "Pending")}</Badge></div>
          </div>
        </div>

        <div className="p-5 rounded-xl border border-slate-200 bg-slate-50">
          <p className="text-sm font-medium text-slate-800 mb-2">SLA</p>
          <div className="text-sm text-slate-600">Breached: <span className="font-semibold text-slate-900">{sla.breachedCount || 0}</span></div>
        </div>

        <div className="p-5 rounded-xl border border-slate-200 bg-slate-50">
          <p className="text-sm font-medium text-slate-800 mb-2">Notes</p>
          <div className="space-y-2 max-h-32 overflow-auto">
            {(notes || []).length === 0 ? <p className="text-xs text-slate-500">No notes yet</p> : null}
            {(notes || []).slice(0, 8).map((n) => (
              <div key={n.id} className="text-xs bg-white border border-slate-200 rounded p-2">
                <div className="text-slate-700">{n.body}</div>
                <div className="text-[10px] text-slate-400 mt-1">{n.createdByName || "Agent"}</div>
              </div>
            ))}
          </div>
          <div className="mt-2 flex gap-2">
            <Input placeholder="Add note" value={newNote} onChange={(e) => setNewNote(e.target.value)} />
            <Button size="sm" onClick={handleAddNote} className="bg-orange-500 hover:bg-orange-600 text-white">Save</Button>
          </div>
        </div>
      </div>
    </div>
  );

  return (
    <div className="relative h-[calc(100vh-7rem)] min-w-0 flex rounded-3xl overflow-hidden border border-slate-200 bg-gradient-to-b from-white to-slate-50 shadow-[0_12px_30px_rgba(15,23,42,0.08)]" data-testid="inbox-page">
      <div className="w-[280px] xl:w-[300px] 2xl:w-[320px] flex-shrink-0 border-r border-slate-200 bg-white flex flex-col">
        <div className="p-5 border-b border-slate-200">
          <div className="flex items-center gap-2 mb-4">
            <h2 className="text-4xl leading-none font-heading font-semibold text-slate-900">Inbox</h2>
            <Badge className="bg-gradient-to-r from-orange-500 to-orange-600 text-white rounded-xl px-3 py-1 shadow-sm border-0">{filterCounts.all}</Badge>
          </div>
          <div className="flex items-center gap-2">
            <div className="relative flex-1">
              <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-400" />
              <Input placeholder="Search conversations..." className="pl-10 rounded-xl border-slate-200 bg-white text-slate-900 placeholder:text-slate-400 h-11" value={searchQuery} onChange={(e) => setSearchQuery(e.target.value)} data-testid="inbox-search" />
            </div>
            <Button variant="outline" size="icon" className="rounded-xl h-11 w-11 border-slate-200 bg-white text-slate-700 hover:bg-slate-50" data-testid="inbox-filter-btn">
              <Filter className="w-4 h-4" />
            </Button>
          </div>
        </div>

        <div className="p-4 border-b border-slate-200">
          <div className="grid grid-cols-3 gap-2 rounded-xl bg-slate-100 p-1">
            <button className={`h-9 rounded-lg text-sm font-medium transition ${activeFilter === "all" ? "bg-orange-500 text-white shadow-sm" : "text-slate-600"}`} onClick={() => setActiveFilter("all")}>All ({filterCounts.all})</button>
            <button className={`h-9 rounded-lg text-sm font-medium transition ${activeFilter === "mine" ? "bg-orange-500 text-white shadow-sm" : "text-slate-600"}`} onClick={() => setActiveFilter("mine")}>Mine ({filterCounts.mine})</button>
            <button className={`h-9 rounded-lg text-sm font-medium transition ${activeFilter === "unassigned" ? "bg-orange-500 text-white shadow-sm" : "text-slate-600"}`} onClick={() => setActiveFilter("unassigned")}>Unassigned ({filterCounts.unassigned})</button>
          </div>
        </div>

        <ScrollArea className="flex-1">
          {filteredConversations.map((conversation) => (
            <div key={conversation.id} className={`p-4 border-b border-slate-100 cursor-pointer transition-colors ${selectedChat?.id === conversation.id ? "bg-orange-50 border-l-4 border-l-orange-500" : "hover:bg-slate-50"}`} onClick={() => setSelectedConversationId(conversation.id)}>
              <div className="flex items-start gap-3">
                <div className="relative">
                  <Avatar className="w-12 h-12">
                    <AvatarFallback className="bg-gradient-to-br from-orange-400 to-orange-600 text-white font-medium">{conversation.avatar}</AvatarFallback>
                  </Avatar>
                  {conversation.channel === "whatsapp" && <div className="absolute -bottom-1 -right-1 w-5 h-5 bg-green-500 rounded-full flex items-center justify-center"><MessageCircle className="w-3 h-3 text-white" /></div>}
                </div>
                <div className="flex-1 min-w-0">
                  <div className="flex items-center justify-between mb-1">
                    <p className="font-medium text-slate-900 truncate">{conversation.name}</p>
                    <span className="text-xs text-slate-500">{formatAgo(conversation.time)}</span>
                  </div>
                  <p className="text-sm text-slate-500 truncate">{conversation.lastMessage}</p>
                  <div className="flex items-center justify-between mt-1">
                    <span className="text-xs text-slate-400">{conversation.phone} {conversation.assignedUserName ? `• ${conversation.assignedUserName}` : "• Unassigned"}</span>
                    {conversation.unread > 0 ? <Badge className="bg-orange-500 text-white border-0">{conversation.unread}</Badge> : null}
                  </div>
                </div>
              </div>
            </div>
          ))}
        </ScrollArea>
      </div>

      <div className="flex-1 min-w-0 flex flex-col bg-slate-50/60">
        <div className="min-h-16 px-4 py-2 border-b border-slate-200 flex flex-wrap items-center justify-between gap-3 bg-white">
          <div className="flex items-center gap-3 min-w-0">
            <Avatar className="w-10 h-10"><AvatarFallback className="bg-gradient-to-br from-orange-400 to-orange-600 text-white font-medium">{selectedChat.avatar}</AvatarFallback></Avatar>
            <div className="max-w-[220px] min-w-0"><p className="font-semibold text-slate-900 text-lg leading-tight truncate">{selectedChat.name}</p><p className="text-xs text-slate-500 truncate">{selectedChat.phone}</p></div>
          </div>
          {typingUsers.length > 0 ? <div className="text-xs text-emerald-600">{typingUsers.join(", ")} typing...</div> : null}
          {!canReplyInSession ? (
            <div className="hidden 2xl:flex items-center gap-2">
              <div className="px-2.5 py-1 rounded-md bg-amber-50 border border-amber-200 text-[11px] text-amber-800 max-w-[220px] truncate">24h session closed. Customer must reply first.</div>
              <select
                className="h-8 rounded-md border border-slate-200 bg-white px-2 text-xs text-slate-700 max-w-[180px]"
                value={selectedTemplateId}
                onChange={(e) => setSelectedTemplateId(e.target.value)}
              >
                {templates.length === 0 ? <option value="">No templates</option> : null}
                {templates.map((t) => (
                  <option key={t.id} value={String(t.id)}>{t.name}</option>
                ))}
              </select>
              {templateParamIndexes.map((idx) => (
                <Input
                  key={idx}
                  className="h-8 text-xs w-28"
                  placeholder={`Var ${idx}`}
                  value={templateVars[idx] || ""}
                  onChange={(e) => setTemplateVars((prev) => ({ ...prev, [idx]: e.target.value }))}
                />
              ))}
              {selectedTemplate ? (
                <div className="hidden 2xl:block text-[11px] text-slate-500 max-w-[220px] truncate">
                  Preview: {previewTemplateBody}
                </div>
              ) : null}
              <Button size="sm" className="h-8 bg-orange-500 hover:bg-orange-600 text-white" onClick={handleSendTemplateFallback}>Send Template</Button>
            </div>
          ) : null}
          <div className="flex items-center gap-1.5 shrink min-w-0 flex-wrap justify-end">
            {shouldShowEnableSoundPrompt ? (
              <Button
                variant="outline"
                size="sm"
                className="rounded-xl h-9 px-3 border-orange-200 text-orange-700 hover:bg-orange-50"
                onClick={requestBrowserNotificationAndSound}
              >
                Enable notification sounds
              </Button>
            ) : null}
            <div className="hidden 2xl:flex items-center gap-2 rounded-xl border border-slate-200 bg-white px-2 h-9">
              <Bell className="w-4 h-4 text-slate-500" />
              <select
                className="h-7 bg-transparent text-xs text-slate-700 outline-none"
                value={notificationStyle}
                onChange={(e) => setNotificationStyle(e.target.value)}
              >
                <option value="whatsapp">WhatsApp</option>
                <option value="classic">Classic</option>
                <option value="soft">Soft</option>
                <option value="double">Double</option>
                <option value="chime">Chime</option>
                <option value="off">Off</option>
              </select>
              <input
                type="range"
                min="0"
                max="2"
                step="0.1"
                value={notificationVolume}
                onChange={(e) => setNotificationVolumeState(Number(e.target.value) || 1)}
                className="w-20"
                title="Notification volume"
              />
              <Button
                type="button"
                variant="ghost"
                size="sm"
                className="h-7 px-2 text-xs text-slate-600"
                onClick={async () => {
                  if (!audioUnlocked) {
                    await requestBrowserNotificationAndSound();
                  }
                  playNotificationSoundRef.current?.(860);
                }}
              >
                Test
              </Button>
            </div>
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button variant="outline" size="sm" className="rounded-xl h-9 px-3 border-slate-200 bg-white text-slate-800 hover:bg-slate-50">
                  DND
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end">
                <DropdownMenuItem onClick={() => setDndUntilUtc(0)}>Off</DropdownMenuItem>
                <DropdownMenuItem onClick={() => setDndUntilUtc(Date.now() + 15 * 60 * 1000)}>15 min</DropdownMenuItem>
                <DropdownMenuItem onClick={() => setDndUntilUtc(Date.now() + 60 * 60 * 1000)}>1 hour</DropdownMenuItem>
                <DropdownMenuItem onClick={() => setDndUntilUtc(Date.now() + 8 * 60 * 60 * 1000)}>8 hours</DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
            <DropdownMenu>
              <DropdownMenuTrigger asChild><Button disabled={assignBusy} variant="outline" size="sm" className="rounded-xl h-9 px-3 border-slate-200 bg-white text-slate-800 hover:bg-slate-50"><UserPlus className="w-4 h-4 mr-1.5" />{assignBusy ? "Assigning..." : "Assign"}</Button></DropdownMenuTrigger>
              <DropdownMenuContent align="end">{teamMembers.length === 0 ? <DropdownMenuItem disabled>No members</DropdownMenuItem> : teamMembers.map((member) => <DropdownMenuItem key={member.id} disabled={assignBusy} onClick={() => handleAssign(member)}>{member.name} ({member.role})</DropdownMenuItem>)}</DropdownMenuContent>
            </DropdownMenu>
            <DropdownMenu>
              <DropdownMenuTrigger asChild><Button disabled={transferBusy} variant="outline" size="sm" className="rounded-xl h-9 px-3 border-slate-200 bg-white text-slate-800 hover:bg-slate-50">{transferBusy ? "Transferring..." : "Transfer"}</Button></DropdownMenuTrigger>
              <DropdownMenuContent align="end">{teamMembers.length === 0 ? <DropdownMenuItem disabled>No members</DropdownMenuItem> : teamMembers.map((member) => <DropdownMenuItem key={member.id} disabled={transferBusy} onClick={() => handleTransfer(member)}>{member.name} ({member.role})</DropdownMenuItem>)}</DropdownMenuContent>
            </DropdownMenu>
            <Button variant="ghost" size="icon" className="h-9 w-9 text-slate-700 hidden xl:inline-flex" onClick={handleCall}><Phone className="w-4 h-4" /></Button>
            <Button variant="ghost" size="icon" className="h-9 w-9 text-slate-700 hidden xl:inline-flex" onClick={handleVideoCall}><Video className="w-4 h-4" /></Button>
            <Button variant="ghost" size="icon" className="h-9 w-9 text-slate-700 hidden xl:inline-flex 2xl:hidden" onClick={() => setShowDetailsDrawer(true)}><Info className="w-4 h-4" /></Button>
            <DropdownMenu>
              <DropdownMenuTrigger asChild><Button variant="ghost" size="icon" className="h-9 w-9 text-slate-700"><MoreVertical className="w-4 h-4" /></Button></DropdownMenuTrigger>
              <DropdownMenuContent align="end">
                <DropdownMenuItem disabled={starBusy} onClick={handleToggleStar}><Star className="w-4 h-4 mr-2" /> {starBusy ? "Updating..." : isStarred ? "Unstar conversation" : "Star conversation"}</DropdownMenuItem>
                <DropdownMenuItem><UserPlus className="w-4 h-4 mr-2" /> Assign to agent</DropdownMenuItem>
                <DropdownMenuItem><Tag className="w-4 h-4 mr-2" /> Add label</DropdownMenuItem>
                <DropdownMenuSeparator />
                <DropdownMenuItem><Archive className="w-4 h-4 mr-2" /> Archive</DropdownMenuItem>
                <DropdownMenuItem className="text-red-600"><Trash2 className="w-4 h-4 mr-2" /> Delete</DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          </div>
        </div>

        <ScrollArea className="flex-1 p-6 bg-slate-50/70">
          <div className="space-y-4 max-w-3xl mx-auto">
            <div className="flex items-center justify-center"><span className="px-3 py-1 bg-white text-xs text-slate-500 rounded-full border border-slate-200">Today</span></div>
            {messages.length === 0 ? <div className="h-[60vh] flex items-center justify-center"><div className="text-center max-w-sm"><div className="w-16 h-16 rounded-2xl mx-auto bg-orange-100 text-orange-600 flex items-center justify-center mb-4"><MessageCircle className="w-8 h-8" /></div><h3 className="text-xl font-semibold text-slate-900">No messages yet</h3><p className="text-slate-500 mt-1">Start conversation with a customer to see messages and actions here.</p></div></div> : null}
            {messages.map((msg) => (
              <div key={msg.id} className={`flex ${msg.sender === "agent" ? "justify-end" : "justify-start"}`}>
                <div className={`group max-w-[70%] ${msg.sender === "agent" ? "chat-bubble-sent text-slate-900" : "chat-bubble-received text-slate-900"} px-4 py-3`} onDoubleClick={() => setReplyTarget(msg)}>
                  {msg.sender === "agent" ? (
                    <div className="text-[11px] text-emerald-700 font-medium mb-1">By {agentDisplayName}</div>
                  ) : null}
                    {renderMessageText(msg, selectedChat?.name || "Customer", agentDisplayName)}
                  {Array.isArray(msg.interactiveButtons) && msg.interactiveButtons.length > 0 ? (
                    <div className="mt-2 space-y-1.5">
                      {msg.interactiveButtons.map((btn, idx) => (
                        <div
                          key={`${msg.id}-btn-${idx}`}
                          className="h-8 rounded-md border border-sky-200 bg-sky-50 text-sky-700 text-xs font-medium flex items-center justify-center"
                        >
                          ↩ {btn}
                        </div>
                      ))}
                    </div>
                  ) : null}
                  {msg.messageType.startsWith("media:") ? <InboundMediaPreview msg={msg} onOpen={() => openInboundMedia(msg)} /> : null}
                  <div className={`flex items-center gap-2 mt-1 ${msg.sender === "agent" ? "justify-end" : ""}`}>
                    <span className="text-xs text-slate-500">{msg.time}</span>
                    {msg.sender === "agent" && getStatusIcon(msg.status)}
                    <button type="button" className="text-[11px] text-slate-400 hover:text-orange-600 opacity-0 group-hover:opacity-100 transition" onClick={() => setReplyTarget(msg)}>Reply</button>
                  </div>
                  {msg.sender === "agent" && msg.status === "failed" ? <p className="text-[11px] text-red-600 mt-1">{msg.lastError || "Send failed"}</p> : null}
                  {msg.sender === "agent" && msg.status === "retryscheduled" && msg.nextRetryAtUtc ? <p className="text-[11px] text-amber-700 mt-1">Retry at {new Date(msg.nextRetryAtUtc).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })}</p> : null}
                </div>
              </div>
            ))}
            <div ref={endMessageRef} />
          </div>
        </ScrollArea>

        <div className="p-4 border-t border-slate-200 bg-white">
          {replyToMessage ? (
            <div className="mb-3 rounded-xl border border-orange-200 bg-orange-50/60 px-3 py-2 flex items-start justify-between gap-3">
              <div className="min-w-0">
                <div className="text-xs font-semibold text-orange-700">Replying to ({getReplyTargetLabel(replyToMessage.sender)} {replyToMessage.time})</div>
                <div className="text-xs text-slate-600 truncate">{replyToMessage.text || "Message"}</div>
              </div>
              <Button type="button" variant="ghost" size="icon" className="h-6 w-6 text-slate-500" onClick={() => setReplyToMessage(null)}>
                <X className="w-3.5 h-3.5" />
              </Button>
            </div>
          ) : null}
          <div className="flex items-end gap-3 min-w-0">
            <div className="flex items-center gap-1 flex-wrap shrink-0 max-w-full">
              <Button variant="ghost" size="icon" className="text-slate-500" onClick={handlePickAttachment}><Paperclip className="w-5 h-5" /></Button>
              <Button variant="ghost" size="icon" className="text-slate-500" onClick={handlePickImage}><Image className="w-5 h-5" /></Button>
              <Button variant="ghost" size="icon" className="text-slate-500" onClick={handlePickDocument}><FileText className="w-5 h-5" /></Button>
              <Button variant="outline" size="sm" className="h-8 px-3 rounded-lg text-xs inline-flex items-center gap-1.5 hidden xl:inline-flex" onClick={toggleTemplatePanel}>
                <FileText className="w-3.5 h-3.5" />
                <span>Attach Template</span>
              </Button>
              <Button variant="outline" size="sm" className="h-8 px-3 rounded-lg text-xs inline-flex items-center gap-1.5 hidden xl:inline-flex" onClick={toggleFaqPanel}>
                <MessageCircle className="w-3.5 h-3.5" />
                <span>Attach Q&A</span>
              </Button>
              <input ref={fileInputRef} type="file" className="hidden" onChange={(e) => handleAttachmentSelected(e)} />
              <input ref={imageInputRef} type="file" accept="image/*" className="hidden" onChange={(e) => handleAttachmentSelected(e, "image")} />
              <input ref={docInputRef} type="file" accept=".pdf,.doc,.docx,.xls,.xlsx,.txt,.zip" className="hidden" onChange={(e) => handleAttachmentSelected(e, "document")} />
            </div>
            <div className="flex-1 min-w-0 relative rounded-2xl border border-slate-200 bg-white">
              <Textarea
                placeholder={canReplyInSession ? "Type a message..." : "24h session closed. Use template message flow."}
                value={message}
                onChange={(e) => handleInputTyping(e.target.value)}
                className="min-h-[70px] max-h-40 resize-none pr-12 text-base leading-6 border-0 focus-visible:ring-0 rounded-2xl bg-transparent text-slate-900 placeholder:text-slate-400"
                disabled={!canReplyInSession}
                onFocus={() => {
                  if (selectedChat?.id) emitTyping(true);
                }}
                onBlur={() => {
                  stopTyping();
                }}
                onKeyDown={(e) => {
                  if (e.key === "Enter" && !e.shiftKey) {
                    e.preventDefault();
                    handleSendMessage();
                  }
                }}
              />
              <Button variant="ghost" size="icon" className="absolute right-2 bottom-3 text-slate-500" onClick={() => setShowEmojiTray((v) => !v)}><Smile className="w-5 h-5" /></Button>
              {showEmojiTray ? (
                <div className="absolute right-3 bottom-14 z-20 rounded-xl border border-slate-200 bg-white shadow-lg p-2 w-80">
                  <Input placeholder="Search emoji..." className="h-8 text-xs mb-2" value={emojiSearch} onChange={(e) => setEmojiSearch(e.target.value)} />
                  <div className="max-h-44 overflow-auto grid grid-cols-10 gap-1">
                    {filteredEmojis.map((e, idx) => (
                      <button key={`${e}-${idx}`} className="w-7 h-7 rounded hover:bg-slate-100 text-lg" onClick={() => handleInsertEmoji(e)}>{e}</button>
                    ))}
                  </div>
                </div>
              ) : null}
              {showTemplateAttach ? (
                <div className="absolute left-3 bottom-16 z-20 rounded-xl border border-slate-200 bg-white shadow-lg p-3 w-[min(420px,calc(100vw-8rem))] max-w-[calc(100vw-8rem)]">
                  <div className="flex items-center justify-between mb-2">
                    <p className="text-xs font-semibold text-slate-700">Attach Template</p>
                    <Button type="button" variant="ghost" size="icon" className="h-6 w-6" onClick={() => setShowTemplateAttach(false)}>
                      <X className="w-3.5 h-3.5" />
                    </Button>
                  </div>
                  <div className="grid grid-cols-2 gap-2 mb-2">
                    <select className="h-9 rounded-md border border-slate-200 bg-white px-2 text-xs text-slate-700" value={selectedTemplateId} onChange={(e) => setSelectedTemplateId(e.target.value)}>
                      {templates.length === 0 ? <option value="">No templates</option> : null}
                      {templates.map((t) => <option key={t.id} value={String(t.id)}>{t.name}</option>)}
                    </select>
                    <Button size="sm" className="h-9 bg-orange-500 hover:bg-orange-600 text-white" onClick={handleSendTemplateFallback} disabled={sendBusy || !selectedTemplate}>Send Template</Button>
                  </div>
                  <div className="flex items-center justify-between gap-2 mb-2">
                    <Button
                      type="button"
                      size="sm"
                      variant="outline"
                      className="h-8 text-xs"
                      onClick={applySuggestedTemplateVars}
                      disabled={templatePresetBusy}
                    >
                      {templatePresetBusy ? "Loading presets..." : "Auto-fill system variables"}
                    </Button>
                    <span className="text-[11px] text-slate-500">{templatePresetTokens.length} presets</span>
                  </div>
                  <div className="grid grid-cols-2 gap-2">
                    {templateParamIndexes.map((idx) => (
                      <div key={idx} className="space-y-1">
                        <Input
                          className="h-8 text-xs"
                          placeholder={`Var ${idx}`}
                          value={templateVars[idx] || ""}
                          onChange={(e) => setTemplateVars((prev) => ({ ...prev, [idx]: e.target.value }))}
                        />
                        <select
                          className="h-7 w-full rounded border border-slate-200 bg-white px-2 text-[11px] text-slate-600"
                          value=""
                          onChange={(e) => {
                            const token = e.target.value;
                            if (!token) return;
                            setTemplateVars((prev) => ({ ...prev, [idx]: `{{${token}}}` }));
                            e.target.value = "";
                          }}
                        >
                          <option value="">Use system variable...</option>
                          {templatePresetTokens.map((token) => (
                            <option key={`${idx}-${token.key}`} value={token.key}>
                              {token.label} ({token.value})
                            </option>
                          ))}
                        </select>
                      </div>
                    ))}
                  </div>
                  {selectedTemplate ? (
                    <div className="mt-2 rounded border border-slate-100 bg-slate-50 px-2 py-1 text-[11px] text-slate-600 line-clamp-3">
                      {previewTemplateBody}
                    </div>
                  ) : null}
                </div>
              ) : null}
              {showFaqAttach ? (
                <div className="absolute left-3 bottom-16 z-20 rounded-xl border border-slate-200 bg-white shadow-lg p-3 w-[min(420px,calc(100vw-8rem))] max-w-[calc(100vw-8rem)]">
                  <div className="flex items-center justify-between mb-2">
                    <p className="text-xs font-semibold text-slate-700">Attach Q&A Answer</p>
                    <Button type="button" variant="ghost" size="icon" className="h-6 w-6" onClick={() => setShowFaqAttach(false)}>
                      <X className="w-3.5 h-3.5" />
                    </Button>
                  </div>
                  <div className="max-h-44 overflow-auto space-y-1">
                    {faqs.length === 0 ? <p className="text-xs text-slate-500">No Q&A items found.</p> : null}
                    {faqs.map((f) => (
                      <button key={f.id} className="w-full text-left rounded-md border border-slate-200 p-2 hover:bg-slate-50" onClick={() => handleAttachFaq(f)}>
                        <div className="text-xs font-medium text-slate-700 truncate">{f.question}</div>
                        <div className="text-[11px] text-slate-500 truncate">{f.answer}</div>
                      </button>
                    ))}
                  </div>
                </div>
              ) : null}
            </div>
            <div className="flex items-center gap-1">
              <Button variant="ghost" size="icon" className={`text-slate-500 ${voiceRecording ? "bg-red-50 text-red-600" : ""}`} onClick={handleVoiceNote}><Mic className="w-5 h-5" /></Button>
              <Button className="bg-gradient-to-r from-orange-500 to-orange-600 hover:from-orange-600 hover:to-orange-700 text-white rounded-xl h-12 w-14 shadow-md shadow-orange-500/30" onClick={handleSendMessage} disabled={!canReplyInSession || sendBusy}><Send className="w-5 h-5" /></Button>
            </div>
          </div>
          {voiceRecording ? (
            <div className="mt-2 text-xs text-red-600">
              Recording... {Math.floor(recordingSeconds / 60).toString().padStart(2, "0")}:{(recordingSeconds % 60).toString().padStart(2, "0")}
            </div>
          ) : null}
          {!voiceRecording && pendingVoiceFile ? (
            <div className="mt-2 text-xs text-emerald-700">
              Voice ready ({voiceMimeType || pendingVoiceFile.type || "audio"}). Click send to deliver.
            </div>
          ) : null}
        </div>
      </div>

      <div className="w-[280px] 2xl:w-[320px] min-w-0 flex-shrink-0 border-l border-slate-200 bg-white hidden 2xl:block overflow-y-auto">
        {detailsPanelContent}
      </div>

      {showDetailsDrawer ? (
        <div className="2xl:hidden absolute inset-0 z-30 flex justify-end">
          <button className="flex-1 bg-slate-900/35" onClick={() => setShowDetailsDrawer(false)} aria-label="Close details panel" />
          <div className="w-[320px] max-w-[92vw] border-l border-slate-200 bg-white overflow-y-auto shadow-2xl">
            <div className="sticky top-0 z-10 flex items-center justify-between border-b border-slate-200 bg-white px-4 py-3">
              <div className="text-sm font-semibold text-slate-900">Conversation Details</div>
              <Button type="button" variant="ghost" size="icon" className="h-8 w-8" onClick={() => setShowDetailsDrawer(false)}>
                <X className="w-4 h-4" />
              </Button>
            </div>
            {detailsPanelContent}
          </div>
        </div>
      ) : null}
    </div>
  );
};

export default InboxPage;
