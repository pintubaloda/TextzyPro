export const mapConversation = (x) => {
  const id = x.id ?? x.Id ?? "";
  const customerName = x.customerName ?? x.CustomerName ?? "";
  const customerPhone = x.customerPhone ?? x.CustomerPhone ?? "";
  const status = x.status ?? x.Status ?? "";
  const lastMessageAtUtc = x.lastMessageAtUtc ?? x.LastMessageAtUtc ?? null;
  const createdAtUtc = x.createdAtUtc ?? x.CreatedAtUtc ?? null;
  const labelsCsv = x.labelsCsv ?? x.LabelsCsv ?? "";
  const assignedUserId = x.assignedUserId ?? x.AssignedUserId ?? "";
  const assignedUserName = x.assignedUserName ?? x.AssignedUserName ?? "";
  return {
    id,
    customerPhone,
    name: customerName || customerPhone || "Conversation",
    avatar: "",
    color: "#F97316",
    online: false,
    unread: Number(x.unreadCount ?? x.UnreadCount ?? 0),
    time: lastMessageAtUtc
      ? new Date(lastMessageAtUtc).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })
      : createdAtUtc
        ? new Date(createdAtUtc).toLocaleDateString()
        : "",
    lastMsg: status || "Conversation",
    typing: false,
    messages: [],
    labels: String(labelsCsv)
      .split(",")
      .map((z) => z.trim())
      .filter(Boolean),
    assignedUserId,
    assignedUserName,
  };
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
    if (m) {
      return {
        kind: "location",
        data: {
          label: m[1]?.trim() || "Shared location",
          lat: m[2],
          lng: m[3],
        },
      };
    }
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

  if (
    raw.startsWith("Unsupported message:") ||
    raw === "Unsupported incoming WhatsApp message type." ||
    raw === "Inbound unsupported message" ||
    type === "unsupported"
  ) {
    return { kind: "unsupported", data: { reason: raw } };
  }

  if (raw.startsWith("Referral:")) {
    return { kind: "referral", data: { headline: raw.replace(/^Referral:\s*/i, "").trim() } };
  }

  return { kind: "", data: null };
};

export const mapMessage = (x) => {
  const rawStatus = String(x.status ?? x.Status ?? "").toLowerCase();
  const sender = rawStatus === "received" ? "customer" : "agent";
  const messageType = String(x.messageType ?? x.MessageType ?? "session");
  let text = String(x.body ?? x.Body ?? "");
  let media = null;
  if (messageType.startsWith("media:")) {
    try {
      media = JSON.parse(String(x.body ?? x.Body ?? "{}"));
    } catch {
      media = null;
    }
    const kind = messageType.split(":")[1] || "media";
    const caption = media?.caption ? ` - ${media.caption}` : "";
    text = `${kind.toUpperCase()}${caption}`;
  }
  if (messageType === "template") {
    const name = String(x.body ?? x.Body ?? "").split("|")[0] || "template";
    text = `Template: ${name}`;
  }
  const createdAt = x.createdAtUtc ?? x.CreatedAtUtc;
  const interactiveButtons = parseInteractiveButtonsFromType(messageType);
  const structured = parseInboundStructured(text, messageType);
  return {
    id: x.id ?? x.Id ?? `${Date.now()}-${Math.random()}`,
    sent: sender === "agent",
    direction: sender,
    text,
    time: createdAt
      ? new Date(createdAt).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })
      : "now",
    createdAtMs: createdAt ? new Date(createdAt).getTime() : null,
    status: rawStatus || "sent",
    messageType,
    interactiveButtons,
    media,
    specialKind: structured.kind,
    specialData: structured.data,
  };
};

export const extractTemplateParamIndexes = (body = "") => {
  const seen = new Set();
  const out = [];
  const matches = String(body).match(/\{\{(\d+)\}\}/g) || [];
  matches.forEach((m) => {
    const idx = Number(String(m).replace(/[{}]/g, ""));
    if (!Number.isFinite(idx) || idx < 1 || seen.has(idx)) return;
    seen.add(idx);
    out.push(idx);
  });
  return out.sort((a, b) => a - b);
};
