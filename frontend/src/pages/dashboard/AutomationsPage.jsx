import { useEffect, useMemo, useRef, useState, useCallback, useReducer } from "react";
import { Link, useLocation, useNavigate } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle, DialogTrigger } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { ScrollArea } from "@/components/ui/scroll-area";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Textarea } from "@/components/ui/textarea";
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "@/components/ui/tooltip";
import { apiDelete, apiGet, apiPost, apiPut, getBillingUsage, getCurrentBillingPlan } from "@/lib/api";
import { toast } from "sonner";
import {
  Plus, Save, Trash2, UploadCloud, MessageCircle, ImageIcon, FileText, Bot, HelpCircle,
  List, Type, GitBranch, Timer, CornerUpRight, UserCheck, Tags, Webhook, OctagonX, MapPin,
  Link as LinkIcon, ZoomIn, ZoomOut, Maximize2, ChevronRight, ChevronDown, Search,
  Settings2, Eye, Play, MoreVertical, Copy, Lock, Unlock, ArrowRight, X, CheckCircle2,
  AlertTriangle, Info, Zap, MousePointer2, Layers, BarChart3, Clock, CheckSquare, Activity, Send
} from "lucide-react";

/* ─── THEME TOKENS ─────────────────────────────────────────────────────────── */
const T = {
  orange: "#F97316",
  orangeHover: "#EA580C",
  orangeLight: "#FFF7ED",
  orangeMid: "#FFEDD5",
  teal: "#14B8A6",
  success: "#22C55E",
  error: "#EF4444",
  warning: "#F59E0B",
  info: "#3B82F6",
  whatsapp: "#25D366",
  canvas: "#f8fafc",
  dots: "#cbd5e1",
  chatBg: "#ece8de",
};

/* ─── NODE LIBRARY ─────────────────────────────────────────────────────────── */
const NODE_SECTIONS = [
  {
    id: "message", label: "Messages", color: "#10b981", bg: "#ecfdf5", border: "#6ee7b7",
    icon: MessageCircle,
    items: ["text", "media", "template", "bot_reply", "faq_reply", "cta_url"],
  },
  {
    id: "input", label: "User Input", color: "#0ea5e9", bg: "#f0f9ff", border: "#7dd3fc",
    icon: HelpCircle,
    items: ["ask_question", "buttons", "list", "capture_input", "location", "form"],
  },
  {
    id: "logic", label: "Logic", color: "#F97316", bg: "#FFF7ED", border: "#FDBA74",
    icon: GitBranch,
    items: ["condition", "faq_condition", "delay", "jump"],
  },
  {
    id: "system", label: "System", color: "#8b5cf6", bg: "#faf5ff", border: "#c4b5fd",
    icon: Zap,
    items: ["handoff", "request_intervention", "tag_user", "webhook", "end"],
  },
];

const NODE_META = {
  text:                 { label: "Text Message",        icon: MessageCircle, hint: "Send plain text reply", color: "#10b981", bg: "#ecfdf5", border: "#6ee7b7" },
  media:                { label: "Media",               icon: ImageIcon,     hint: "Send image/video/doc",  color: "#10b981", bg: "#ecfdf5", border: "#6ee7b7" },
  template:             { label: "Template",            icon: FileText,      hint: "Use approved template", color: "#10b981", bg: "#ecfdf5", border: "#6ee7b7" },
  bot_reply:            { label: "Bot Reply",           icon: Bot,           hint: "Smart reply with options", color: "#10b981", bg: "#ecfdf5", border: "#6ee7b7" },
  faq_reply:            { label: "FAQ Reply",           icon: HelpCircle,    hint: "Send matched FAQ answer", color: "#10b981", bg: "#ecfdf5", border: "#6ee7b7" },
  cta_url:              { label: "CTA Button",          icon: LinkIcon,      hint: "Text + URL buttons",    color: "#10b981", bg: "#ecfdf5", border: "#6ee7b7" },
  ask_question:         { label: "Ask Question",        icon: HelpCircle,    hint: "Capture user intent",   color: "#0ea5e9", bg: "#f0f9ff", border: "#7dd3fc" },
  buttons:              { label: "Buttons",             icon: Type,          hint: "Interactive choices",    color: "#0ea5e9", bg: "#f0f9ff", border: "#7dd3fc" },
  list:                 { label: "List Menu",           icon: List,          hint: "Scrollable list picker", color: "#0ea5e9", bg: "#f0f9ff", border: "#7dd3fc" },
  capture_input:        { label: "Capture Input",       icon: Type,          hint: "Save user response",     color: "#0ea5e9", bg: "#f0f9ff", border: "#7dd3fc" },
  location:             { label: "Location",            icon: MapPin,        hint: "Request GPS location",   color: "#0ea5e9", bg: "#f0f9ff", border: "#7dd3fc" },
  form:                 { label: "Form",                icon: FileText,      hint: "Multi-field capture",    color: "#0ea5e9", bg: "#f0f9ff", border: "#7dd3fc" },
  condition:            { label: "Condition",           icon: GitBranch,     hint: "True / False branching", color: "#F97316", bg: "#FFF7ED", border: "#FDBA74" },
  faq_condition:        { label: "FAQ Condition",       icon: HelpCircle,    hint: "Branch on FAQ match",    color: "#F97316", bg: "#FFF7ED", border: "#FDBA74" },
  delay:                { label: "Delay",               icon: Timer,         hint: "Wait then continue",     color: "#F97316", bg: "#FFF7ED", border: "#FDBA74" },
  jump:                 { label: "Jump To",             icon: CornerUpRight, hint: "Go to specific node",    color: "#F97316", bg: "#FFF7ED", border: "#FDBA74" },
  handoff:              { label: "Assign Agent",        icon: UserCheck,     hint: "Transfer to human",      color: "#8b5cf6", bg: "#faf5ff", border: "#c4b5fd" },
  request_intervention: { label: "Request Help",        icon: UserCheck,     hint: "Escalate to team",       color: "#8b5cf6", bg: "#faf5ff", border: "#c4b5fd" },
  tag_user:             { label: "Tag User",            icon: Tags,          hint: "Apply contact labels",   color: "#8b5cf6", bg: "#faf5ff", border: "#c4b5fd" },
  webhook:              { label: "Webhook / API",       icon: Webhook,       hint: "Call external service",  color: "#8b5cf6", bg: "#faf5ff", border: "#c4b5fd" },
  end:                  { label: "End Flow",            icon: OctagonX,      hint: "Terminate the flow",     color: "#ef4444", bg: "#fef2f2", border: "#fca5a5" },
  start:                { label: "Start",               icon: Play,          hint: "Flow entry point",       color: "#22c55e", bg: "#f0fdf4", border: "#86efac" },
};

/* ─── UTILITIES ─────────────────────────────────────────────────────────────── */
function uid(prefix = "node") {
  return `${prefix}_${Math.random().toString(36).slice(2, 9)}`;
}

function createNode(type, x = 300, y = 200) {
  const base = {
    id: uid(type), type, name: NODE_META[type]?.label || type,
    x, y, next: "", onTrue: "", onFalse: "", config: {}
  };
  if (type === "text")                 base.config = { body: "Welcome 👋" };
  if (type === "media")                base.config = { body: "", mediaType: "image", mediaUrl: "" };
  if (type === "template")             base.config = { templateName: "", languageCode: "en", body: "", parameters: [] };
  if (type === "buttons")              base.config = { body: "Please choose:", buttons: ["Support", "Sales", "Accounts"] };
  if (type === "list")                 base.config = { headerText: "Select an option", sections: [{ name: "Options", items: ["Option A", "Option B"] }] };
  if (type === "ask_question")         base.config = { question: "How can I help you?" };
  if (type === "capture_input")        base.config = { variable: "user_input", validation: "text", prompt: "Please enter your response:", maxAttempts: 2, minLength: "", maxLength: "", regex: "" };
  if (type === "condition")            base.config = { field: "message", operator: "contains", value: "support" };
  if (type === "delay")                base.config = { seconds: 2 };
  if (type === "handoff")              base.config = { queue: "support" };
  if (type === "location")             base.config = { prompt: "Please share your location." };
  if (type === "form")                 base.config = { title: "Lead Form", fields: [{ key: "name", label: "Full Name", type: "text", required: true }, { key: "email", label: "Email", type: "email", required: true }] };
  if (type === "tag_user")             base.config = { tags: ["new-lead"] };
  if (type === "webhook")              base.config = { method: "POST", url: "", headers: {}, body: '{"phone":"{{phone}}"}' };
  if (type === "request_intervention") base.config = { queue: "support", message: "Our team will be with you shortly." };
  if (type === "cta_url")              base.config = { body: "Choose an option", ctaButtons: [{ text: "Visit Website", url: "https://" }] };
  if (type === "bot_reply")            base.config = { replyMode: "simple", simpleText: "How can I help you?", mediaText: "", mediaUrl: "", advancedType: "quick_reply", buttons: ["Sales", "Support", "Accounts"], ctaButtons: [{ text: "Website", url: "https://" }], listHeader: "Select", listItems: [{ title: "FAQ", subtitle: "Help center" }] };
  if (type === "jump")                 base.config = {};
  if (type === "faq_condition")        return { ...base, type: "condition", name: "FAQ Found?", config: { field: "faq_answer", operator: "not_equals", value: "" } };
  if (type === "faq_reply")            return { ...base, type: "text", name: "FAQ Answer", config: { body: "{{faq_answer}}" } };
  return base;
}

function buildSupportDefinition(company = "your company") {
  return {
    trigger: { type: "keyword", keywords: ["hi", "hello"] },
    startNodeId: "start_1",
    nodes: [
      { id: "start_1",   type: "start",     name: "Start",          next: "welcome_1", x: 120,  y: 200, config: {} },
      { id: "welcome_1", type: "bot_reply", name: "Welcome Reply",  next: "route_1",  x: 380,  y: 200, config: { replyMode: "advanced", advancedType: "quick_reply", simpleText: `Welcome to ${company}! How can I help?`, buttons: ["Support", "Sales", "Accounts"] } },
      { id: "route_1",   type: "condition", name: "Route: Support?", x: 640,  y: 140, config: { field: "message", operator: "contains", value: "support" }, onTrue: "support_1", onFalse: "route_2" },
      { id: "route_2",   type: "condition", name: "Route: Sales?",   x: 640,  y: 340, config: { field: "message", operator: "contains", value: "sales" }, onTrue: "sales_1", onFalse: "faq_1" },
      { id: "support_1", type: "text",      name: "Support Intro",  next: "handoff_1", x: 900,  y: 80,  config: { body: "Connecting you to our support team. Tell me your query." } },
      { id: "sales_1",   type: "text",      name: "Sales Intro",    next: "handoff_2", x: 900,  y: 280, config: { body: "Connecting you to our sales team. How can we help?" } },
      { id: "faq_1",     type: "condition", name: "FAQ Found?",      x: 900,  y: 460, config: { field: "faq_answer", operator: "not_equals", value: "" }, onTrue: "faq_ans", onFalse: "fallback" },
      { id: "faq_ans",   type: "text",      name: "FAQ Answer",     next: "end_1",   x: 1160, y: 380, config: { body: "{{faq_answer}}" } },
      { id: "fallback",  type: "text",      name: "Fallback",        next: "handoff_1", x: 1160, y: 540, config: { body: "Let me connect you with a live agent." } },
      { id: "handoff_1", type: "handoff",   name: "Assign Support", next: "end_1",   x: 1160, y: 80,  config: { queue: "support" } },
      { id: "handoff_2", type: "handoff",   name: "Assign Sales",   next: "end_1",   x: 1160, y: 280, config: { queue: "sales" } },
      { id: "end_1",     type: "end",       name: "End",            next: "",        x: 1420, y: 300, config: {} },
    ],
  };
}

function computeEdges(nodes) {
  const out = [];
  nodes.forEach((n) => {
    if (n.next)    out.push({ from: n.id, to: n.next,    label: "next",  color: T.teal });
    if (n.onTrue)  out.push({ from: n.id, to: n.onTrue,  label: "True",  color: T.success });
    if (n.onFalse) out.push({ from: n.id, to: n.onFalse, label: "False", color: T.error });
    if (n.onFailure) out.push({ from: n.id, to: n.onFailure, label: "Fail", color: T.error });
  });
  return out;
}

function extractTemplateVars(text) {
  const matches = [...String(text || "").matchAll(/\{\{(\d+)\}\}/g)];
  return [...new Set(matches.map((m) => Number(m[1])))].sort((a, b) => a - b);
}

/* ─── NODE DIMENSIONS ───────────────────────────────────────────────────────── */
const NODE_W = 220;
const NODE_H_BASE = 72;

/* ─── FLOW COLORS ───────────────────────────────────────────────────────────── */
const FLOW_COLORS = {
  published: "bg-emerald-100 text-emerald-700 border-emerald-200",
  draft:     "bg-amber-100 text-amber-700 border-amber-200",
  archived:  "bg-slate-100 text-slate-500 border-slate-200",
};

function getFlowSelectionKey() {
  try {
    const raw = localStorage.getItem("textzy.session");
    const session = raw ? JSON.parse(raw) : {};
    const tenantSlug = String(session?.tenantSlug || "default").trim().toLowerCase() || "default";
    return `textzy.automation.selectedFlow:${tenantSlug}`;
  } catch {
    return "textzy.automation.selectedFlow:default";
  }
}

const SUPPORTED_PUBLISH_NODE_TYPES = new Set([
  "start",
  "text","text_message","textmessage","send_text","message",
  "media",
  "template",
  "buttons",
  "list",
  "ask_question",
  "capture_input",
  "bot_reply","botreply",
  "cta_url",
  "condition","split",
  "delay","wait",
  "assign_agent","assignagent","handoff",
  "request_intervention","requesthelp",
  "tag_user","taguser",
  "webhook","api_call",
  "jump",
  "subflow",
  "end",
]);

/* ═══════════════════════════════════════════════════════════════════════════════
   WHATSAPP PREVIEW
═══════════════════════════════════════════════════════════════════════════════ */
function WhatsAppPreview({ node }) {
  if (!node) return <div className="text-xs text-slate-400 italic p-4 text-center">Select a node to preview</div>;
  const config = node.config || {};
  const { type } = node;

  const isBotReply = type === "bot_reply";
  const replyMode = config.replyMode || "simple";
  const advType = config.advancedType || "quick_reply";
  const text = replyMode === "media" ? config.mediaText : config.simpleText || config.body || config.question || config.prompt || "Preview";
  const buttons = Array.isArray(config.buttons) ? config.buttons.filter(Boolean) : [];
  const ctaButtons = Array.isArray(config.ctaButtons) ? config.ctaButtons.filter((b) => b?.text) : [];
  const listItems = Array.isArray(config.listItems) ? config.listItems.filter((i) => i?.title) : [];
  const formFields = Array.isArray(config.fields) ? config.fields : [];

  const now = new Date();
  const timeStr = `${now.getHours()}:${String(now.getMinutes()).padStart(2, "0")} ${now.getHours() >= 12 ? "PM" : "AM"}`;

  return (
    <div className="rounded-2xl overflow-hidden" style={{ background: T.chatBg }}>
      {/* Chat header */}
      <div className="flex items-center gap-2 px-3 py-2" style={{ background: "#1f2c34" }}>
        <div className="w-8 h-8 rounded-full flex items-center justify-center text-white text-xs font-bold" style={{ background: T.whatsapp }}>
          <Bot size={14} />
        </div>
        <div>
          <div className="text-white text-xs font-semibold">Bot</div>
          <div className="text-xs" style={{ color: T.whatsapp }}>online</div>
        </div>
      </div>

      {/* Message bubble */}
      <div className="p-3 space-y-1">
        {type === "end" ? (
          <div className="text-center py-3">
            <OctagonX size={20} className="mx-auto mb-1 text-red-400" />
            <div className="text-xs text-slate-500">Flow ends here</div>
          </div>
        ) : type === "start" ? (
          <div className="text-center py-3">
            <Play size={20} className="mx-auto mb-1 text-emerald-500" />
            <div className="text-xs text-slate-500">Flow starts here</div>
          </div>
        ) : type === "delay" ? (
          <div className="text-center py-3">
            <Timer size={20} className="mx-auto mb-1 text-orange-400" />
            <div className="text-xs text-slate-600">Wait <strong>{config.seconds || 0}s</strong> then continue</div>
          </div>
        ) : type === "condition" ? (
          <div className="text-center py-3 space-y-1">
            <GitBranch size={20} className="mx-auto text-orange-400" />
            <div className="text-xs text-slate-700 font-medium">if <code className="bg-white rounded px-1">{config.field}</code> {config.operator} <code className="bg-white rounded px-1">"{config.value}"</code></div>
            <div className="flex justify-center gap-3 mt-2">
              <span className="text-[10px] rounded-full px-2 py-0.5 font-semibold" style={{ background: "#dcfce7", color: "#15803d" }}>✓ True →</span>
              <span className="text-[10px] rounded-full px-2 py-0.5 font-semibold" style={{ background: "#fee2e2", color: "#b91c1c" }}>✗ False →</span>
            </div>
          </div>
        ) : type === "handoff" || type === "request_intervention" ? (
          <div className="text-center py-3">
            <UserCheck size={20} className="mx-auto mb-1 text-purple-400" />
            <div className="text-xs text-slate-600">Assign to queue: <strong>{config.queue || "—"}</strong></div>
            {config.message && <div className="text-xs text-slate-500 mt-1 italic">"{config.message}"</div>}
          </div>
        ) : type === "tag_user" ? (
          <div className="text-center py-3">
            <Tags size={18} className="mx-auto mb-1 text-purple-400" />
            <div className="flex flex-wrap gap-1 justify-center mt-1">
              {(config.tags || []).map((t, i) => (
                <span key={i} className="text-[10px] bg-purple-100 text-purple-700 rounded-full px-2 py-0.5">{t}</span>
              ))}
            </div>
          </div>
        ) : type === "webhook" ? (
          <div className="py-2">
            <div className="flex items-center gap-1 mb-1">
              <span className="text-[10px] font-bold rounded px-1.5 py-0.5 text-white" style={{ background: config.method === "GET" ? "#0ea5e9" : "#F97316" }}>{config.method || "POST"}</span>
              <span className="text-[10px] text-slate-600 truncate">{config.url || "https://..."}</span>
            </div>
          </div>
        ) : type === "jump" ? (
          <div className="text-center py-3">
            <CornerUpRight size={18} className="mx-auto mb-1 text-orange-400" />
            <div className="text-xs text-slate-600">Jump to: <strong>{node.next || "—"}</strong></div>
          </div>
        ) : type === "form" ? (
          <div className="bg-white rounded-xl overflow-hidden shadow-sm">
            <div className="px-3 py-2 border-b border-slate-100">
              <div className="text-xs font-semibold text-slate-800">{config.title || "Form"}</div>
            </div>
            {formFields.slice(0, 4).map((f, i) => (
              <div key={i} className="border-b border-slate-100 px-3 py-2">
                <div className="text-[10px] text-slate-500 mb-0.5">{f.label} {f.required && <span className="text-red-400">*</span>}</div>
                <div className="h-3 rounded bg-slate-100 w-full" />
              </div>
            ))}
            <div className="px-3 py-2">
              <div className="rounded-lg text-center text-[11px] font-semibold py-1.5 text-white" style={{ background: T.orange }}>Submit</div>
            </div>
          </div>
        ) : (
          <div className="bg-white rounded-xl rounded-tl-sm overflow-hidden shadow-sm max-w-[240px]">
            {type === "media" && config.mediaUrl && (
              <div className="bg-slate-100 h-24 flex items-center justify-center">
                <ImageIcon size={24} className="text-slate-300" />
              </div>
            )}
            <div className="px-3 py-2">
              <p className="text-xs text-slate-800 whitespace-pre-wrap leading-relaxed">{text || "…"}</p>
              <div className="text-right text-[10px] text-slate-400 mt-1">{timeStr} ✓✓</div>
            </div>
            {/* Quick reply buttons */}
            {(type === "buttons" || (isBotReply && replyMode === "advanced" && advType === "quick_reply")) && buttons.length > 0 && (
              <div className="border-t border-slate-100">
                {buttons.slice(0, 3).map((b, i) => (
                  <button key={i} className="w-full border-b border-slate-100 last:border-0 px-3 py-2 text-center text-[12px] font-medium hover:bg-slate-50 transition-colors" style={{ color: "#0ea5e9" }}>
                    ↩ {b}
                  </button>
                ))}
              </div>
            )}
            {/* CTA Buttons */}
            {(type === "cta_url" || (isBotReply && replyMode === "advanced" && advType === "cta")) && ctaButtons.length > 0 && (
              <div className="border-t border-slate-100">
                {ctaButtons.slice(0, 2).map((b, i) => (
                  <button key={i} className="w-full border-b border-slate-100 last:border-0 px-3 py-2 text-center text-[12px] font-medium hover:bg-slate-50 transition-colors" style={{ color: "#3b82f6" }}>
                    🔗 {b.text}
                  </button>
                ))}
              </div>
            )}
            {/* List */}
            {(isBotReply && replyMode === "advanced" && advType === "list") && listItems.length > 0 && (
              <div className="border-t border-slate-100">
                <div className="px-3 py-1.5 text-[10px] font-semibold text-slate-500 uppercase tracking-wide">{config.listHeader || "Options"}</div>
                {listItems.slice(0, 3).map((item, i) => (
                  <div key={i} className="border-t border-slate-100 px-3 py-2">
                    <div className="text-[11px] font-medium text-slate-800">{item.title}</div>
                    {item.subtitle && <div className="text-[10px] text-slate-400">{item.subtitle}</div>}
                  </div>
                ))}
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  );
}

/* ═══════════════════════════════════════════════════════════════════════════════
   CANVAS NODE CARD
═══════════════════════════════════════════════════════════════════════════════ */
function CanvasNode({ node, isSelected, onSelect, onStartConnect, onConnectTo, onDelete, isDragging, onDragStart, zoom }) {
  const meta = NODE_META[node.type] || {};
  const Icon = meta.icon || MessageCircle;
  const config = node.config || {};

  // Preview text
  const previewText = config.simpleText || config.body || config.question || config.prompt || meta.hint || "";
  const buttons = Array.isArray(config.buttons) ? config.buttons.filter(Boolean).slice(0, 3) : [];
  const ctaButtons = Array.isArray(config.ctaButtons) ? config.ctaButtons.filter((b) => b?.text).slice(0, 2) : [];
  const listItems = Array.isArray(config.listItems) ? config.listItems.filter((i) => i?.title).slice(0, 2) : [];
  const formFields = Array.isArray(config.fields) ? config.fields.slice(0, 3) : [];

  const isCondition = node.type === "condition";
  const isEnd = node.type === "end";
  const isStart = node.type === "start";

  return (
    <div
      data-node-id={node.id}
      style={{
        position: "absolute",
        left: node.x,
        top: node.y,
        width: NODE_W,
        userSelect: "none",
        cursor: isDragging ? "grabbing" : "grab",
        zIndex: isSelected ? 20 : 10,
        transform: isSelected ? "scale(1.02)" : "scale(1)",
        transition: isDragging ? "none" : "transform 0.1s ease, box-shadow 0.1s ease",
      }}
      onMouseDown={(e) => {
        e.stopPropagation();
        if (e.target.closest("[data-connect-handle]") || e.target.closest("[data-delete-btn]")) return;
        onDragStart(e, node.id);
        onSelect(node.id);
      }}
      onMouseUp={() => onConnectTo(node.id)}
    >
      <div
        style={{
          borderRadius: 12,
          border: `2px solid ${isSelected ? meta.color : meta.border || "#e2e8f0"}`,
          background: "white",
          boxShadow: isSelected
            ? `0 0 0 3px ${meta.color}30, 0 8px 24px rgba(0,0,0,0.12)`
            : "0 2px 8px rgba(0,0,0,0.08)",
          overflow: "hidden",
        }}
      >
        {/* Header */}
        <div
          style={{ background: meta.bg, borderBottom: `1px solid ${meta.border}`, padding: "8px 10px" }}
          className="flex items-center justify-between"
        >
          <div className="flex items-center gap-2 min-w-0">
            <div className="w-6 h-6 rounded-md flex items-center justify-center flex-shrink-0" style={{ background: meta.color }}>
              <Icon size={12} color="white" />
            </div>
            <div className="min-w-0">
              <div className="text-[10px] font-semibold uppercase tracking-wide" style={{ color: meta.color }}>{meta.label}</div>
              <div className="text-[11px] font-medium text-slate-700 truncate">{node.name}</div>
            </div>
          </div>
          <button
            data-delete-btn
            className="w-5 h-5 rounded flex items-center justify-center opacity-0 group-hover:opacity-100 hover:bg-red-100 hover:text-red-500 transition-all"
            style={{ opacity: isSelected ? 1 : 0, color: "#94a3b8" }}
            onClick={(e) => { e.stopPropagation(); onDelete(node.id); }}
            title="Delete node"
          >
            <X size={10} />
          </button>
        </div>

        {/* Body */}
        <div style={{ padding: "8px 10px", minHeight: 32 }}>
          {isStart && <div className="text-[11px] text-slate-400 italic">Entry point</div>}
          {isEnd && <div className="text-[11px] text-red-400 font-medium">Flow terminates</div>}

          {(node.type === "text" || node.type === "ask_question" || node.type === "location") && previewText && (
            <div className="text-[11px] text-slate-600 line-clamp-2">{previewText}</div>
          )}
          {node.type === "delay" && (
            <div className="flex items-center gap-1 text-[11px] text-orange-600">
              <Timer size={11} /> Wait <strong>{config.seconds || 0}s</strong>
            </div>
          )}
          {node.type === "condition" && (
            <div className="text-[11px] text-slate-600 font-mono">
              if {config.field} <span className="text-orange-500">{config.operator}</span> "{config.value}"
            </div>
          )}
          {(node.type === "bot_reply" || node.type === "media") && previewText && (
            <div className="text-[11px] text-slate-600 line-clamp-2">{previewText}</div>
          )}
          {node.type === "webhook" && (
            <div className="flex items-center gap-1">
              <span className="text-[9px] font-bold px-1 py-0.5 rounded text-white" style={{ background: T.orange }}>{config.method || "POST"}</span>
              <span className="text-[10px] text-slate-400 truncate">{config.url || "https://..."}</span>
            </div>
          )}
          {node.type === "tag_user" && (
            <div className="flex flex-wrap gap-1">
              {(config.tags || []).slice(0, 3).map((t, i) => (
                <span key={i} className="text-[9px] bg-purple-100 text-purple-600 rounded-full px-1.5 py-0.5">{t}</span>
              ))}
            </div>
          )}
          {node.type === "handoff" && (
            <div className="text-[11px] text-slate-500">Queue: <strong>{config.queue || "—"}</strong></div>
          )}
          {node.type === "capture_input" && (
            <div className="space-y-1">
              <div className="text-[11px] text-slate-500">Save to: <strong className="font-mono">{"{{"}{ config.variable || "user_input" }{"}}"}</strong></div>
              <div className="text-[10px] text-slate-500">
                {(config.validation || "text").toUpperCase()} · Max attempts: <strong>{Number(config.maxAttempts || 0) || "∞"}</strong>
              </div>
              {Number(config.maxAttempts || 0) > 0 && node.onFailure ? (
                <div className="text-[10px] text-red-500">Escalate → {node.onFailure}</div>
              ) : null}
            </div>
          )}
          {node.type === "form" && formFields.length > 0 && (
            <div className="space-y-0.5">
              {formFields.map((f, i) => (
                <div key={i} className="text-[10px] text-slate-500">{f.label || f.key} {f.required && <span className="text-red-400">*</span>}</div>
              ))}
            </div>
          )}
          {/* Mini button previews */}
          {buttons.length > 0 && !isCondition && (
            <div className="mt-1.5 space-y-0.5">
              {buttons.slice(0, 2).map((b, i) => (
                <div key={i} className="h-5 rounded text-[9px] flex items-center justify-center font-medium border" style={{ borderColor: "#7dd3fc", color: "#0ea5e9", background: "#f0f9ff" }}>↩ {b}</div>
              ))}
              {buttons.length > 2 && <div className="text-[9px] text-slate-400 text-center">+{buttons.length - 2} more</div>}
            </div>
          )}
          {ctaButtons.length > 0 && (
            <div className="mt-1.5 space-y-0.5">
              {ctaButtons.map((b, i) => (
                <div key={i} className="h-5 rounded text-[9px] flex items-center justify-center font-medium border" style={{ borderColor: "#93c5fd", color: "#3b82f6", background: "#eff6ff" }}>🔗 {b.text}</div>
              ))}
            </div>
          )}
        </div>

        {/* Connection handle(s) */}
        {isCondition ? (
          <div className="flex border-t border-slate-100">
            <button
              data-connect-handle="true"
              title="Drag to connect True path"
              onMouseDown={(e) => { e.stopPropagation(); onStartConnect(node.id, e, "true"); }}
              className="flex-1 py-1.5 text-[10px] font-semibold flex items-center justify-center gap-1 hover:bg-emerald-50 transition-colors"
              style={{ color: T.success, borderRight: "1px solid #f1f5f9" }}
            >
              <div className="w-2 h-2 rounded-full" style={{ background: T.success }} />
              True
            </button>
            <button
              data-connect-handle="true"
              title="Drag to connect False path"
              onMouseDown={(e) => { e.stopPropagation(); onStartConnect(node.id, e, "false"); }}
              className="flex-1 py-1.5 text-[10px] font-semibold flex items-center justify-center gap-1 hover:bg-red-50 transition-colors"
              style={{ color: T.error }}
            >
              <div className="w-2 h-2 rounded-full" style={{ background: T.error }} />
              False
            </button>
          </div>
        ) : !isEnd && (
          <div className="border-t border-slate-100 flex justify-end px-2 py-1">
            <button
              data-connect-handle="true"
              title="Drag to connect to next node"
              onMouseDown={(e) => { e.stopPropagation(); onStartConnect(node.id, e, "next"); }}
              className="w-6 h-6 rounded-full border-2 hover:scale-110 transition-transform flex items-center justify-center"
              style={{ borderColor: meta.color, background: "white" }}
            >
              <div className="w-2 h-2 rounded-full" style={{ background: meta.color }} />
            </button>
          </div>
        )}
      </div>
    </div>
  );
}

/* ═══════════════════════════════════════════════════════════════════════════════
   SVG EDGES
═══════════════════════════════════════════════════════════════════════════════ */
function EdgePath({ edge, nodes, pan }) {
  const fromNode = nodes.find((n) => n.id === edge.from);
  const toNode = nodes.find((n) => n.id === edge.to);
  if (!fromNode || !toNode) return null;

  const isCondition = fromNode.type === "condition";

  // Source point: right side of node
  // For condition: True/False buttons are at the bottom of the card
  // The card body is approx NODE_H_BASE tall, then the True/False row is ~28px
  let sx, sy;
  if (isCondition) {
    // Both True and False handles are on the right edge of their half of the bottom bar
    sx = fromNode.x + NODE_W;
    sy = fromNode.y + NODE_H_BASE + (edge.label === "True" ? 10 : 24);
  } else {
    sx = fromNode.x + NODE_W;
    sy = fromNode.y + NODE_H_BASE / 2;
  }

  // Target point (left center)
  const tx = toNode.x;
  const ty = toNode.y + NODE_H_BASE / 2;

  const dx = Math.abs(tx - sx);
  const cx1 = sx + Math.max(40, dx * 0.5);
  const cx2 = tx - Math.max(40, dx * 0.5);
  const pathD = `M ${sx} ${sy} C ${cx1} ${sy}, ${cx2} ${ty}, ${tx} ${ty}`;

  const midX = (sx + tx) / 2;
  const midY = (sy + ty) / 2;

  const color = edge.label === "True" ? T.success : edge.label === "False" ? T.error : T.teal;

  return (
    <g style={{ cursor: "pointer" }}>
      <defs>
        <marker id={`arrow-${color.replace('#','')}`} markerWidth="8" markerHeight="8" refX="6" refY="3" orient="auto">
          <path d="M0,0 L0,6 L8,3 z" fill={color} opacity="0.9" />
        </marker>
      </defs>
      {/* Invisible wider path for easier click */}
      <path d={pathD} fill="none" stroke="transparent" strokeWidth="12" />
      <path d={pathD} fill="none" stroke={color} strokeWidth="2"
        strokeDasharray={edge.label === "next" ? "none" : "6 3"} opacity="0.8"
        markerEnd={`url(#arrow-${color.replace('#','')})`} />
      {/* Label */}
      {edge.label !== "next" && (
        <g>
          <rect x={midX - 18} y={midY - 9} width={36} height={18} rx="5" fill={color} opacity="0.92" />
          <text x={midX} y={midY + 4} textAnchor="middle" fontSize="9" fill="white" fontWeight="700">{edge.label}</text>
        </g>
      )}
    </g>
  );
}

/* ═══════════════════════════════════════════════════════════════════════════════
   NODE PANEL (right panel config)
═══════════════════════════════════════════════════════════════════════════════ */
function NodePanel({ node, nodes, onUpdate, onDelete, onDuplicate, onDisconnect }) {
  if (!node) {
    return (
      <div className="flex flex-col items-center justify-center h-full text-center p-6 space-y-3">
        <div className="w-14 h-14 rounded-2xl bg-slate-100 flex items-center justify-center">
          <MousePointer2 size={24} className="text-slate-400" />
        </div>
        <div>
          <div className="font-semibold text-slate-700">No node selected</div>
          <div className="text-xs text-slate-400 mt-1">Click a node on the canvas to configure it, or drag a new node from the palette.</div>
        </div>
      </div>
    );
  }

  const meta = NODE_META[node.type] || {};
  const Icon = meta.icon || MessageCircle;
  const config = node.config || {};

  const upd = (patch) => onUpdate(node.id, { config: { ...config, ...patch } });

  return (
    <ScrollArea className="h-full">
      <div className="p-4 space-y-4">
        {/* Node Header */}
        <div className="flex items-center gap-3 p-3 rounded-xl" style={{ background: meta.bg, border: `1px solid ${meta.border}` }}>
          <div className="w-9 h-9 rounded-xl flex items-center justify-center" style={{ background: meta.color }}>
            <Icon size={16} color="white" />
          </div>
          <div className="flex-1 min-w-0">
            <div className="text-[10px] font-bold uppercase tracking-wider mb-0.5" style={{ color: meta.color }}>{meta.label}</div>
            <Input
              value={node.name || ""}
              onChange={(e) => onUpdate(node.id, { name: e.target.value })}
              className="h-6 text-xs border-0 p-0 bg-transparent font-semibold text-slate-800 focus-visible:ring-0"
              placeholder="Node name..."
            />
          </div>
        </div>

        {/* Config Fields */}
        <div className="space-y-3">
          {/* TEXT */}
          {node.type === "text" && (
            <Field label="Message Body" required>
              <Textarea rows={3} value={config.body || ""} onChange={(e) => upd({ body: e.target.value })} placeholder="Your message here..." />
            </Field>
          )}

          {/* ASK QUESTION */}
          {node.type === "ask_question" && (
            <Field label="Question" required>
              <Textarea rows={2} value={config.question || ""} onChange={(e) => upd({ question: e.target.value })} />
            </Field>
          )}

          {/* CAPTURE INPUT */}
          {node.type === "capture_input" && (
            <>
              <Field label="Save to Variable" required>
                <div className="relative">
                  <span className="absolute left-3 top-2 text-slate-400 text-xs">{"{{"}{ }</span>
                  <Input className="pl-6 pr-6 font-mono text-xs" value={config.variable || ""} onChange={(e) => upd({ variable: e.target.value })} placeholder="user_input" />
                  <span className="absolute right-3 top-2 text-slate-400 text-xs">{"}}"}</span>
                </div>
              </Field>
              <Field label="Validation Type">
                <SelectField value={config.validation || "text"} onChange={(v) => upd({ validation: v })}
                  options={[["text","Text / Any"],["number","Number"],["email","Email"],["phone","Phone"],["regex","Regex"]]} />
              </Field>
              <Field label="Max Attempts (0 = unlimited)">
                <Input
                  type="number"
                  min={0}
                  value={String(config.maxAttempts ?? 2)}
                  onChange={(e) => upd({ maxAttempts: Number(e.target.value || 0) })}
                />
              </Field>
              <div className="grid grid-cols-2 gap-2">
                <Field label="Min Length">
                  <Input
                    type="number"
                    min={0}
                    value={String(config.minLength ?? "")}
                    onChange={(e) => upd({ minLength: e.target.value })}
                  />
                </Field>
                <Field label="Max Length">
                  <Input
                    type="number"
                    min={0}
                    value={String(config.maxLength ?? "")}
                    onChange={(e) => upd({ maxLength: e.target.value })}
                  />
                </Field>
              </div>
              {(config.validation || "text") === "regex" && (
                <Field label="Regex Pattern" hint="Example: ^[A-Z]{3}[0-9]{3}$">
                  <Input
                    className="font-mono text-xs"
                    value={config.regex || ""}
                    onChange={(e) => upd({ regex: e.target.value })}
                    placeholder="^[A-Z]{3}[0-9]{3}$"
                  />
                </Field>
              )}
              <Field label="Prompt Message">
                <Textarea rows={2} value={config.prompt || ""} onChange={(e) => upd({ prompt: e.target.value })} />
              </Field>
              <div className="grid grid-cols-1 gap-2">
                <Field label="✅ Valid → Node">
                  <NodeSelector value={node.onTrue || ""} nodes={nodes} nodeId={node.id} onChange={(v) => onUpdate(node.id, { onTrue: v })} />
                </Field>
                <Field label="↻ Invalid (retry) → Node">
                  <NodeSelector value={node.onFalse || ""} nodes={nodes} nodeId={node.id} onChange={(v) => onUpdate(node.id, { onFalse: v })} />
                </Field>
                <Field label="🛑 Max attempts reached → Node">
                  <NodeSelector value={node.onFailure || ""} nodes={nodes} nodeId={node.id} onChange={(v) => onUpdate(node.id, { onFailure: v })} />
                </Field>
              </div>
            </>
          )}

          {/* MEDIA */}
          {node.type === "media" && (
            <>
              <Field label="Media Type">
                <SelectField value={config.mediaType || "image"} onChange={(v) => upd({ mediaType: v })}
                  options={[["image","🖼 Image"],["video","🎥 Video"],["document","📄 Document"],["audio","🎵 Audio"]]} />
              </Field>
              <Field label="Media URL" required>
                <Input value={config.mediaUrl || ""} onChange={(e) => upd({ mediaUrl: e.target.value })} placeholder="https://..." />
              </Field>
              <Field label="Caption">
                <Textarea rows={2} value={config.body || ""} onChange={(e) => upd({ body: e.target.value })} />
              </Field>
            </>
          )}

          {/* TEMPLATE */}
          {node.type === "template" && (
            <>
              <Field label="Template Name" required>
                <Input value={config.templateName || ""} onChange={(e) => upd({ templateName: e.target.value })} placeholder="approved_template_name" />
              </Field>
              <Field label="Language Code">
                <Input value={config.languageCode || "en"} onChange={(e) => upd({ languageCode: e.target.value })} placeholder="en" />
              </Field>
              <Field label="Template Body">
                <Textarea rows={3} value={config.body || ""} onChange={(e) => upd({ body: e.target.value })} placeholder="Hello {{1}}, your order {{2}} is ready." />
              </Field>
              <Field label="Parameters (JSON)">
                <JsonField
                  value={config.parameters || []}
                  onChange={(v) => upd({ parameters: v })}
                  placeholder={`[{"index":1,"value":"{{name}}"},{"index":2,"value":"{{order_id}}"}]`}
                  rows={4}
                />
              </Field>
              {(() => {
                const vars = extractTemplateVars(config.body);
                const params = Array.isArray(config.parameters) ? config.parameters : [];
                const missing = vars.filter((v) => !params.some((p) => Number(p?.index) === v));
                return missing.length > 0 ? (
                  <div className="rounded-lg p-2 text-xs flex items-start gap-2" style={{ background: "#fffbeb", border: "1px solid #fde68a", color: "#92400e" }}>
                    <AlertTriangle size={12} className="mt-0.5 flex-shrink-0" />
                    Missing mapping for: {missing.map((n) => `{{${n}}}`).join(", ")}
                  </div>
                ) : vars.length > 0 ? (
                  <div className="rounded-lg p-2 text-xs flex items-start gap-2" style={{ background: "#f0fdf4", border: "1px solid #86efac", color: "#166534" }}>
                    <CheckCircle2 size={12} className="mt-0.5 flex-shrink-0" />
                    All template variables mapped correctly
                  </div>
                ) : null;
              })()}
            </>
          )}

          {/* BUTTONS */}
          {node.type === "buttons" && (
            <>
              <Field label="Message Body">
                <Textarea rows={2} value={config.body || ""} onChange={(e) => upd({ body: e.target.value })} />
              </Field>
              <Field label="Buttons" hint="Up to 3 quick reply buttons">
                <ButtonListEditor
                  buttons={config.buttons || []}
                  onChange={(buttons) => upd({ buttons })}
                  max={3}
                />
              </Field>
            </>
          )}

          {/* LIST */}
          {node.type === "list" && (
            <>
              <Field label="Header Text">
                <Input value={config.headerText || ""} onChange={(e) => upd({ headerText: e.target.value })} />
              </Field>
              <Field label="Sections (JSON)">
                <JsonField value={config.sections || []} onChange={(v) => upd({ sections: v })} rows={5}
                  placeholder={`[{"name":"Options","items":["A","B","C"]}]`} />
              </Field>
            </>
          )}

          {/* LOCATION */}
          {node.type === "location" && (
            <Field label="Prompt Message">
              <Textarea rows={2} value={config.prompt || ""} onChange={(e) => upd({ prompt: e.target.value })} />
            </Field>
          )}

          {/* FORM */}
          {node.type === "form" && (
            <>
              <Field label="Form Title">
                <Input value={config.title || ""} onChange={(e) => upd({ title: e.target.value })} />
              </Field>
              <Field label="Fields (JSON)" hint="Array of {key, label, type, required}">
                <JsonField value={config.fields || []} onChange={(v) => upd({ fields: v })} rows={6}
                  placeholder={`[{"key":"name","label":"Full Name","type":"text","required":true}]`} />
              </Field>
            </>
          )}

          {/* CTA URL */}
          {node.type === "cta_url" && (
            <>
              <Field label="Message Body">
                <Textarea rows={2} value={config.body || ""} onChange={(e) => upd({ body: e.target.value })} />
              </Field>
              <Field label="CTA Buttons" hint="Up to 2 URL buttons">
                <CtaButtonEditor buttons={config.ctaButtons || []} onChange={(v) => upd({ ctaButtons: v })} max={2} />
              </Field>
            </>
          )}

          {/* BOT REPLY */}
          {node.type === "bot_reply" && (
            <>
              <Field label="Reply Mode">
                <SelectField value={config.replyMode || "simple"} onChange={(v) => upd({ replyMode: v })}
                  options={[["simple","💬 Simple Text"],["media","🖼 Media Message"],["advanced","⚡ Advanced (Buttons / List)"]]} />
              </Field>

              {config.replyMode !== "media" && (
                <Field label="Message Text" required>
                  <Textarea rows={3} value={config.simpleText || ""} onChange={(e) => upd({ simpleText: e.target.value })} />
                </Field>
              )}

              {config.replyMode === "media" && (
                <>
                  <Field label="Caption">
                    <Textarea rows={2} value={config.mediaText || ""} onChange={(e) => upd({ mediaText: e.target.value })} />
                  </Field>
                  <Field label="Image URL">
                    <Input value={config.mediaUrl || ""} onChange={(e) => upd({ mediaUrl: e.target.value })} placeholder="https://..." />
                  </Field>
                </>
              )}

              {config.replyMode === "advanced" && (
                <>
                  <Field label="Advanced Type">
                    <SelectField value={config.advancedType || "quick_reply"} onChange={(v) => upd({ advancedType: v })}
                      options={[["quick_reply","↩ Quick Reply Buttons"],["cta","🔗 CTA URL Buttons"],["list","📋 List Message"]]} />
                  </Field>
                  {config.advancedType === "quick_reply" && (
                    <Field label="Buttons" hint="Up to 3">
                      <ButtonListEditor buttons={config.buttons || []} onChange={(v) => upd({ buttons: v })} max={3} />
                    </Field>
                  )}
                  {config.advancedType === "cta" && (
                    <Field label="CTA Buttons" hint="Up to 2">
                      <CtaButtonEditor buttons={config.ctaButtons || []} onChange={(v) => upd({ ctaButtons: v })} max={2} />
                    </Field>
                  )}
                  {config.advancedType === "list" && (
                    <>
                      <Field label="List Header">
                        <Input value={config.listHeader || ""} onChange={(e) => upd({ listHeader: e.target.value })} />
                      </Field>
                      <Field label="List Items (JSON)">
                        <JsonField value={config.listItems || []} onChange={(v) => upd({ listItems: v })} rows={4}
                          placeholder={`[{"title":"FAQ","subtitle":"Help center"}]`} />
                      </Field>
                    </>
                  )}
                </>
              )}
            </>
          )}

          {/* CONDITION */}
          {node.type === "condition" && (
            <>
              <div className="p-3 rounded-xl text-xs space-y-3" style={{ background: "#fffbeb", border: "1px solid #fde68a" }}>
                <div className="font-semibold text-amber-800 flex items-center gap-1.5"><GitBranch size={12} />Condition Logic</div>
                <Field label="Variable / Field">
                  <Input value={config.field || ""} onChange={(e) => upd({ field: e.target.value })} placeholder="message, user_input, phone..." className="text-xs" />
                </Field>
                <Field label="Operator">
                  <SelectField value={config.operator || "contains"} onChange={(v) => upd({ operator: v })}
                    options={[["contains","contains"],["not_contains","does not contain"],["equals","equals"],["not_equals","does not equal"],["starts_with","starts with"],["ends_with","ends with"],["is_empty","is empty"],["is_not_empty","is not empty"]]} />
                </Field>
                <Field label="Value">
                  <Input value={config.value || ""} onChange={(e) => upd({ value: e.target.value })} placeholder="support, yes, 1..." className="text-xs" />
                </Field>
              </div>
              {/* Connection targets */}
              <div className="grid grid-cols-2 gap-2">
                <Field label="✅ True → Node">
                  <NodeSelector value={node.onTrue || ""} nodes={nodes} nodeId={node.id} onChange={(v) => onUpdate(node.id, { onTrue: v })} />
                </Field>
                <Field label="❌ False → Node">
                  <NodeSelector value={node.onFalse || ""} nodes={nodes} nodeId={node.id} onChange={(v) => onUpdate(node.id, { onFalse: v })} />
                </Field>
              </div>
            </>
          )}

          {/* DELAY */}
          {node.type === "delay" && (
            <Field label="Wait Duration">
              <div className="flex gap-2 items-center">
                <Input type="number" min="1" value={String(config.seconds ?? 2)} onChange={(e) => upd({ seconds: Number(e.target.value) })} className="w-24" />
                <span className="text-sm text-slate-500">seconds</span>
              </div>
            </Field>
          )}

          {/* JUMP */}
          {node.type === "jump" && (
            <Field label="Jump To Node">
              <NodeSelector value={node.next || ""} nodes={nodes} nodeId={node.id} onChange={(v) => onUpdate(node.id, { next: v })} />
            </Field>
          )}

          {/* HANDOFF */}
          {(node.type === "handoff" || node.type === "request_intervention") && (
            <>
              <Field label="Assign to Queue">
                <Input value={config.queue || ""} onChange={(e) => upd({ queue: e.target.value })} placeholder="support, sales, accounts..." />
              </Field>
              {node.type === "request_intervention" && (
                <Field label="Message to User">
                  <Textarea rows={2} value={config.message || ""} onChange={(e) => upd({ message: e.target.value })} />
                </Field>
              )}
            </>
          )}

          {/* TAG USER */}
          {node.type === "tag_user" && (
            <Field label="Tags" hint="Comma-separated">
              <Input
                value={(config.tags || []).join(", ")}
                onChange={(e) => upd({ tags: e.target.value.split(",").map((x) => x.trim()).filter(Boolean) })}
                placeholder="new-lead, vip, support-needed"
              />
            </Field>
          )}

          {/* WEBHOOK */}
          {node.type === "webhook" && (
            <>
              <div className="grid grid-cols-2 gap-2">
                <Field label="Method">
                  <SelectField value={config.method || "POST"} onChange={(v) => upd({ method: v })}
                    options={[["GET","GET"],["POST","POST"],["PUT","PUT"],["PATCH","PATCH"],["DELETE","DELETE"]]} />
                </Field>
              </div>
              <Field label="URL" required>
                <Input value={config.url || ""} onChange={(e) => upd({ url: e.target.value })} placeholder="https://api.example.com/webhook" />
              </Field>
              <Field label="Request Body (JSON)">
                <Textarea rows={4} value={config.body || ""} onChange={(e) => upd({ body: e.target.value })} className="font-mono text-xs" />
              </Field>
            </>
          )}

          {/* Next node selector for non-condition, non-end, non-jump */}
          {!["condition","end","jump","start"].includes(node.type) && (
            <Field label="Next Node" hint="Or drag the connector handle">
              <NodeSelector value={node.next || ""} nodes={nodes} nodeId={node.id} onChange={(v) => onUpdate(node.id, { next: v })} />
            </Field>
          )}
        </div>

        {/* Connection status */}
        {node.type !== "end" && (
          <div className="rounded-xl border border-slate-200 p-3 space-y-2">
            <div className="text-[10px] font-bold uppercase tracking-wide text-slate-400">Connections</div>
            {node.type === "condition" ? (
              <div className="space-y-1.5">
                <div className="flex items-center justify-between">
                  <span className="text-xs text-slate-600 flex items-center gap-1">
                    <div className="w-2 h-2 rounded-full" style={{ background: T.success }} />True →
                    <span className="font-mono text-[10px] text-slate-400">{node.onTrue ? nodes.find((n)=>n.id===node.onTrue)?.name || node.onTrue : "not connected"}</span>
                  </span>
                  {node.onTrue && <button className="text-[10px] text-red-400 hover:text-red-600" onClick={() => onDisconnect(node.id, "onTrue")}>✕</button>}
                </div>
                <div className="flex items-center justify-between">
                  <span className="text-xs text-slate-600 flex items-center gap-1">
                    <div className="w-2 h-2 rounded-full" style={{ background: T.error }} />False →
                    <span className="font-mono text-[10px] text-slate-400">{node.onFalse ? nodes.find((n)=>n.id===node.onFalse)?.name || node.onFalse : "not connected"}</span>
                  </span>
                  {node.onFalse && <button className="text-[10px] text-red-400 hover:text-red-600" onClick={() => onDisconnect(node.id, "onFalse")}>✕</button>}
                </div>
              </div>
            ) : node.type === "capture_input" ? (
              <div className="space-y-1.5">
                <div className="flex items-center justify-between">
                  <span className="text-xs text-slate-600 flex items-center gap-1">
                    <div className="w-2 h-2 rounded-full" style={{ background: T.success }} />Valid →
                    <span className="font-mono text-[10px] text-slate-400">{node.onTrue ? nodes.find((n)=>n.id===node.onTrue)?.name || node.onTrue : "not connected"}</span>
                  </span>
                  {node.onTrue && <button className="text-[10px] text-red-400 hover:text-red-600" onClick={() => onDisconnect(node.id, "onTrue")}>×</button>}
                </div>
                <div className="flex items-center justify-between">
                  <span className="text-xs text-slate-600 flex items-center gap-1">
                    <div className="w-2 h-2 rounded-full" style={{ background: T.warning }} />Invalid →
                    <span className="font-mono text-[10px] text-slate-400">{node.onFalse ? nodes.find((n)=>n.id===node.onFalse)?.name || node.onFalse : "not connected"}</span>
                  </span>
                  {node.onFalse && <button className="text-[10px] text-red-400 hover:text-red-600" onClick={() => onDisconnect(node.id, "onFalse")}>×</button>}
                </div>
                <div className="flex items-center justify-between">
                  <span className="text-xs text-slate-600 flex items-center gap-1">
                    <div className="w-2 h-2 rounded-full" style={{ background: T.error }} />Max attempts →
                    <span className="font-mono text-[10px] text-slate-400">{node.onFailure ? nodes.find((n)=>n.id===node.onFailure)?.name || node.onFailure : "not connected"}</span>
                  </span>
                  {node.onFailure && <button className="text-[10px] text-red-400 hover:text-red-600" onClick={() => onDisconnect(node.id, "onFailure")}>×</button>}
                </div>
              </div>
            ) : (
              <div className="flex items-center justify-between">
                <span className="text-xs text-slate-600 flex items-center gap-1">
                  <div className="w-2 h-2 rounded-full" style={{ background: T.teal }} />Next →
                  <span className="font-mono text-[10px] text-slate-400">{node.next ? nodes.find((n)=>n.id===node.next)?.name || node.next : "not connected"}</span>
                </span>
                {node.next && <button className="text-[10px] text-red-400 hover:text-red-600" onClick={() => onDisconnect(node.id, "next")}>✕</button>}
              </div>
            )}
          </div>
        )}

        {/* Actions */}
        <div className="flex gap-2">
          <Button variant="outline" size="sm" className="flex-1 text-xs gap-1" onClick={() => onDuplicate(node.id)}>
            <Copy size={12} />Duplicate
          </Button>
          {node.type !== "start" && (
            <Button variant="destructive" size="sm" className="flex-1 text-xs gap-1" onClick={() => onDelete(node.id)}>
              <Trash2 size={12} />Delete
            </Button>
          )}
        </div>
        <div className="text-[10px] text-slate-400 text-center">
          Del key to delete · Ctrl+D to duplicate · Esc to deselect
        </div>
      </div>
    </ScrollArea>
  );
}

/* ─── Helper Form Components ─────────────────────────────────────────────── */
function Field({ label, hint, required, children }) {
  return (
    <div className="space-y-1">
      <div className="flex items-center justify-between">
        <Label className="text-xs font-semibold text-slate-700">
          {label}{required && <span className="text-red-400 ml-0.5">*</span>}
        </Label>
        {hint && <span className="text-[10px] text-slate-400">{hint}</span>}
      </div>
      {children}
    </div>
  );
}

function SelectField({ value, onChange, options }) {
  return (
    <Select value={value} onValueChange={onChange}>
      <SelectTrigger className="h-8 text-xs"><SelectValue /></SelectTrigger>
      <SelectContent>
        {options.map(([v, l]) => <SelectItem key={v} value={v} className="text-xs">{l}</SelectItem>)}
      </SelectContent>
    </Select>
  );
}

function JsonField({ value, onChange, rows = 4, placeholder = "" }) {
  const [raw, setRaw] = useState(JSON.stringify(value, null, 2));
  const [err, setErr] = useState(false);

  useEffect(() => {
    setRaw(JSON.stringify(value, null, 2));
  }, [value]);

  const handleChange = (v) => {
    setRaw(v);
    try { onChange(JSON.parse(v)); setErr(false); }
    catch { setErr(true); }
  };

  return (
    <div>
      <Textarea rows={rows} value={raw} onChange={(e) => handleChange(e.target.value)}
        className={`font-mono text-xs ${err ? "border-red-400 focus-visible:ring-red-400" : ""}`} placeholder={placeholder} />
      {err && <div className="text-[10px] text-red-500 mt-0.5">Invalid JSON</div>}
    </div>
  );
}

function ButtonListEditor({ buttons, onChange, max = 3 }) {
  const list = Array.isArray(buttons) ? buttons : [];
  const move = (from, to) => {
    if (to < 0 || to >= list.length) return;
    const next = [...list];
    const [item] = next.splice(from, 1);
    next.splice(to, 0, item);
    onChange(next);
  };
  return (
    <div className="space-y-1.5">
      {list.map((btn, i) => (
        <div key={i} className="flex gap-1">
          <Input className="h-7 text-xs flex-1" value={btn} onChange={(e) => {
            const next = [...list]; next[i] = e.target.value; onChange(next);
          }} placeholder={`Button ${i + 1}`} />
          <button className="w-7 h-7 rounded border border-slate-200 flex items-center justify-center text-slate-400 hover:text-red-500 hover:border-red-300"
            onClick={() => onChange(list.filter((_, j) => j !== i))}>
            <X size={12} />
          </button>
          <button className="w-7 h-7 rounded border border-slate-200 flex items-center justify-center text-slate-400 hover:text-slate-600"
            onClick={() => move(i, i - 1)} title="Move up">
            ↑
          </button>
          <button className="w-7 h-7 rounded border border-slate-200 flex items-center justify-center text-slate-400 hover:text-slate-600"
            onClick={() => move(i, i + 1)} title="Move down">
            ↓
          </button>
        </div>
      ))}
      {list.length < max && (
        <button className="w-full h-7 border border-dashed border-slate-300 rounded text-xs text-slate-500 hover:border-orange-400 hover:text-orange-500 transition-colors"
          onClick={() => onChange([...list, ""])}>
          <Plus size={11} className="inline mr-1" />Add Button
        </button>
      )}
    </div>
  );
}

function CtaButtonEditor({ buttons, onChange, max = 2 }) {
  const list = Array.isArray(buttons) ? buttons : [];
  const move = (from, to) => {
    if (to < 0 || to >= list.length) return;
    const next = [...list];
    const [item] = next.splice(from, 1);
    next.splice(to, 0, item);
    onChange(next);
  };
  return (
    <div className="space-y-2">
      {list.map((btn, i) => (
        <div key={i} className="rounded-lg border border-slate-200 p-2 space-y-1">
          <div className="flex gap-1">
            <Input className="h-7 text-xs flex-1" value={btn.text || ""} placeholder="Button text"
              onChange={(e) => { const next = [...list]; next[i] = { ...btn, text: e.target.value }; onChange(next); }} />
            <button className="w-7 h-7 rounded border border-slate-200 flex items-center justify-center text-slate-400 hover:text-red-500"
              onClick={() => onChange(list.filter((_, j) => j !== i))}>
              <X size={12} />
            </button>
            <button className="w-7 h-7 rounded border border-slate-200 flex items-center justify-center text-slate-400 hover:text-slate-600"
              onClick={() => move(i, i - 1)} title="Move up">
              ↑
            </button>
            <button className="w-7 h-7 rounded border border-slate-200 flex items-center justify-center text-slate-400 hover:text-slate-600"
              onClick={() => move(i, i + 1)} title="Move down">
              ↓
            </button>
          </div>
          <Input className="h-7 text-xs" value={btn.url || ""} placeholder="https://..." 
            onChange={(e) => { const next = [...list]; next[i] = { ...btn, url: e.target.value }; onChange(next); }} />
        </div>
      ))}
      {list.length < max && (
        <button className="w-full h-7 border border-dashed border-slate-300 rounded text-xs text-slate-500 hover:border-orange-400 hover:text-orange-500 transition-colors"
          onClick={() => onChange([...list, { text: "", url: "https://" }])}>
          <Plus size={11} className="inline mr-1" />Add CTA Button
        </button>
      )}
    </div>
  );
}

function NodeSelector({ value, nodes, nodeId, onChange }) {
  return (
    <Select value={value || "__none__"} onValueChange={(v) => onChange(v === "__none__" ? "" : v)}>
      <SelectTrigger className="h-8 text-xs"><SelectValue placeholder="Select node..." /></SelectTrigger>
      <SelectContent>
        <SelectItem value="__none__" className="text-xs text-slate-400">— None —</SelectItem>
        {nodes.filter((n) => n.id !== nodeId).map((n) => (
          <SelectItem key={n.id} value={n.id} className="text-xs">
            <span className="text-slate-400 mr-1">{NODE_META[n.type]?.label}</span> {n.name}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}

/* ─── HISTORY REDUCER (undo/redo) ───────────────────────────────────────────── */
const MAX_HISTORY = 50;

function historyReducer(state, action) {
  switch (action.type) {
    case "PUSH": {
      const past = [...state.past, state.present].slice(-MAX_HISTORY);
      return { past, present: action.nodes, future: [] };
    }
    case "UNDO": {
      if (!state.past.length) return state;
      const previous = state.past[state.past.length - 1];
      return { past: state.past.slice(0, -1), present: previous, future: [state.present, ...state.future] };
    }
    case "REDO": {
      if (!state.future.length) return state;
      const next = state.future[0];
      return { past: [...state.past, state.present], present: next, future: state.future.slice(1) };
    }
    case "RESET":
      return { past: [], present: action.nodes, future: [] };
    default:
      return state;
  }
}

/* ─── FLOW VALIDATOR ─────────────────────────────────────────────────────────── */
function validateFlow(nodes) {
  const errors = [];
  const warnings = [];
  const ids = new Set(nodes.map((n) => n.id));

  if (!nodes.length) { errors.push("Flow has no nodes."); return { errors, warnings }; }

  const hasStart = nodes.some((n) => n.type === "start");
  if (!hasStart) warnings.push("No Start node — flow won't trigger correctly.");

  const hasEnd = nodes.some((n) => n.type === "end");
  if (!hasEnd) warnings.push("No End node — flow may loop indefinitely.");

  nodes.forEach((n) => {
    if (n.next && !ids.has(n.next)) errors.push(`Node "${n.name}" has a broken 'next' connection.`);
    if (n.onTrue && !ids.has(n.onTrue)) errors.push(`Node "${n.name}" has a broken 'True' connection.`);
    if (n.onFalse && !ids.has(n.onFalse)) errors.push(`Node "${n.name}" has a broken 'False' connection.`);
    if (n.type === "condition" && !n.onTrue && !n.onFalse) warnings.push(`Condition "${n.name}" has no branches connected.`);
    if (n.type === "capture_input" && !n.onTrue && !n.next) warnings.push(`Capture input "${n.name}" has no valid path connected.`);
    if (n.type === "capture_input" && !n.onFalse && !n.next) warnings.push(`Capture input "${n.name}" has no invalid retry path connected.`);
    if (n.type === "capture_input" && Number(n?.config?.maxAttempts || 0) > 0 && !n.onFailure) warnings.push(`Capture input "${n.name}" has maxAttempts but no onFailure escalation path.`);
    if (n.type === "webhook" && !n.config?.url) warnings.push(`Webhook "${n.name}" has no URL set.`);
    if (n.type === "template" && !n.config?.templateName) errors.push(`Template "${n.name}" has no template name.`);
    if (n.type === "text" && !n.config?.body?.trim()) warnings.push(`Text node "${n.name}" has no message body.`);
    if (["text","bot_reply","buttons","cta_url","ask_question"].includes(n.type) && !n.next && n.type !== "end") {
      // Only warn if no outgoing edge at all
      const hasOut = n.next || n.onTrue || n.onFalse;
      if (!hasOut) warnings.push(`Node "${n.name}" has no outgoing connection.`);
    }
  });

  return { errors, warnings };
}


export default function AutomationsPage() {
  const location = useLocation();
  const navigate = useNavigate();
  const mode = location.pathname.endsWith("/workflow") ? "workflow"
    : location.pathname.endsWith("/qa") ? "qa" : "overview";

  /* ── Data state ── */
  const [flows, setFlows] = useState([]);
  const [selectedFlowId, setSelectedFlowId] = useState("");
  const [versions, setVersions] = useState([]);
  const [limits, setLimits] = useState(null);
  const [billingUsage, setBillingUsage] = useState({});
  const [billingLimits, setBillingLimits] = useState({});
  const [showCreate, setShowCreate] = useState(false);
  const [createForm, setCreateForm] = useState({ name: "", description: "", companyName: "" });
  const [showEditFlow, setShowEditFlow] = useState(false);
  const [editFlowForm, setEditFlowForm] = useState({ name: "", description: "", isActive: true });

  /* ── History-aware node state ── */
  const [history, dispatch] = useReducer(historyReducer, { past: [], present: [], future: [] });
  const nodes = history.present;

  const setNodes = useCallback((updater) => {
    const next = typeof updater === "function" ? updater(history.present) : updater;
    dispatch({ type: "PUSH", nodes: next });
  }, [history.present]);

  const undoNodes = () => dispatch({ type: "UNDO" });
  const redoNodes = () => dispatch({ type: "REDO" });
  const resetNodes = useCallback((ns) => dispatch({ type: "RESET", nodes: ns }), []);

  const canUndo = history.past.length > 0;
  const canRedo = history.future.length > 0;
  const [selectedNodeId, setSelectedNodeId] = useState("");
  const [triggerKeywords, setTriggerKeywords] = useState("hi,hello,HI,Hello");
  const [outside24h, setOutside24h] = useState(false);
  const [searchQuery, setSearchQuery] = useState("");
  const [activeSection, setActiveSection] = useState("message"); // default open
  const [showPreview, setShowPreview] = useState(true);
  const [showVersions, setShowVersions] = useState(false);
  const [isDirty, setIsDirty] = useState(false);
  const [lastSaved, setLastSaved] = useState(null);
  const validation = useMemo(() => validateFlow(nodes), [nodes]);

  /* ── Canvas state ── */
  const canvasRef = useRef(null);
  const [zoom, setZoom] = useState(1);
  const [pan, setPan] = useState({ x: 40, y: 40 });
  const [isPanning, setIsPanning] = useState(false);
  const panStart = useRef(null);
  const [draggingNodeId, setDraggingNodeId] = useState(null);
  const dragOffset = useRef({ x: 0, y: 0 });
  const [dragConnect, setDragConnect] = useState(null);

  /* ── FAQ state ── */
  const [faqItems, setFaqItems] = useState([]);
  const [faqForm, setFaqForm] = useState({ question: "", answer: "", category: "", isActive: true });
  const [editingFaqId, setEditingFaqId] = useState(null);
  const [triggerAuditSummary, setTriggerAuditSummary] = useState(null);

  const normalizeFlow = useCallback((f) => ({
    id: f?.id ?? f?.Id ?? "",
    name: f?.name ?? f?.Name ?? "",
    description: f?.description ?? f?.Description ?? "",
    channel: f?.channel ?? f?.Channel ?? "waba",
    triggerType: f?.triggerType ?? f?.TriggerType ?? "keyword",
    triggerConfigJson: f?.triggerConfigJson ?? f?.TriggerConfigJson ?? "{}",
    isActive: !!(f?.isActive ?? f?.IsActive),
    lifecycleStatus: f?.lifecycleStatus ?? f?.LifecycleStatus ?? "draft",
    createdAtUtc: f?.createdAtUtc ?? f?.CreatedAtUtc ?? null,
    updatedAtUtc: f?.updatedAtUtc ?? f?.UpdatedAtUtc ?? null,
    runs: Number(f?.runs ?? f?.Runs ?? 0),
    failedRuns: Number(f?.failedRuns ?? f?.FailedRuns ?? 0),
    successRate: Number(f?.successRate ?? f?.SuccessRate ?? 0),
    lastRunAtUtc: f?.lastRunAtUtc ?? f?.LastRunAtUtc ?? null,
    versions: Number(f?.versions ?? f?.Versions ?? 0),
    latestVersion: Number(f?.latestVersion ?? f?.LatestVersion ?? 0),
  }), []);

  const normalizeVersion = useCallback((v) => ({
    id: v?.id ?? v?.Id ?? "",
    flowId: v?.flowId ?? v?.FlowId ?? "",
    versionNumber: Number(v?.versionNumber ?? v?.VersionNumber ?? 0),
    status: v?.status ?? v?.Status ?? "draft",
    definitionJson: v?.definitionJson ?? v?.DefinitionJson ?? "{}",
    changeNote: v?.changeNote ?? v?.ChangeNote ?? "",
    createdAtUtc: v?.createdAtUtc ?? v?.CreatedAtUtc ?? null,
    publishedAtUtc: v?.publishedAtUtc ?? v?.PublishedAtUtc ?? null,
  }), []);

  const parseDefinitionNodes = useCallback((raw) => {
    if (!raw) return [];
    try {
      let parsed = raw;
      if (typeof parsed === "string") parsed = JSON.parse(parsed);
      if (typeof parsed === "string") parsed = JSON.parse(parsed); // handle double-encoded payloads
      if (parsed && Array.isArray(parsed.nodes)) return parsed.nodes;
      return [];
    } catch {
      return [];
    }
  }, []);

  const selectedFlow = useMemo(() => flows.find((f) => String(f.id) === String(selectedFlowId)) || null, [flows, selectedFlowId]);
  const selectedNode = useMemo(() => nodes.find((n) => n.id === selectedNodeId) || null, [nodes, selectedNodeId]);
  const edges = useMemo(() => computeEdges(nodes), [nodes]);
  const selectedFlowStatus = useMemo(() => {
    const flowStatus = String(selectedFlow?.lifecycleStatus || "").trim().toLowerCase();
    const latestVersionStatus = String(versions?.[0]?.status || "").trim().toLowerCase();
    if (latestVersionStatus === "published") return "published";
    if (flowStatus) return flowStatus;
    if (latestVersionStatus) return latestVersionStatus;
    return "draft";
  }, [selectedFlow?.lifecycleStatus, versions]);

  const flowLimit = Number(billingLimits.flows || 0);
  const chatbotLimit = Number(billingLimits.chatbots || 0);
  // In this screen, keep "used" aligned with visible tenant flows to avoid
  // mismatch when billing counters are ahead of flow list refresh/migration.
  const flowUsed = Number(flows.length || 0);
  const activeBots = Number(billingUsage.chatbots || flows.filter((x) => x.lifecycleStatus === "published").length || 0);
  const canCreateFlow = flowLimit <= 0 || flowUsed < flowLimit;
  const canPublishBot = chatbotLimit <= 0 || activeBots < chatbotLimit;

  /* ── API ── */
  const loadAll = useCallback(async () => {
    try {
      const [flowsRes, limitsRes] = await Promise.allSettled([
        apiGet("/api/automation/flows"),
        apiGet("/api/automation/limits"),
      ]);

      const flowsRaw = flowsRes.status === "fulfilled" ? (flowsRes.value || []) : [];
      const limitsRaw = limitsRes.status === "fulfilled" ? (limitsRes.value || null) : null;

      const normalizedFlows = (flowsRaw || []).map(normalizeFlow);
      setFlows(normalizedFlows);
      setLimits(limitsRaw);
      if (normalizedFlows.length) {
        setSelectedFlowId((prev) => {
          const current = String(prev || "");
          const saved = (() => {
            try {
              return String(localStorage.getItem(getFlowSelectionKey()) || "");
            } catch {
              return "";
            }
          })();
          const desired = current || saved;
          const exists = normalizedFlows.some((x) => String(x.id) === desired);
          return exists ? desired : String(normalizedFlows[0].id);
        });
      }

      if (flowsRes.status === "rejected") {
        toast.error("Failed to load automation bots");
      }
      if (limitsRes.status === "rejected") {
        toast.error("Automation limits unavailable");
      }

      // Load secondary data in background to keep first paint fast.
      Promise.all([
        apiGet("/api/automation/faq").catch(() => []),
        getBillingUsage().catch(() => ({ values: {} })),
        getCurrentBillingPlan().catch(() => ({ plan: { limits: {} } })),
        apiGet("/api/automation/trigger-audit/summary?days=7").catch(() => null),
      ]).then(([q, usageRes, planRes, triggerSummary]) => {
        setFaqItems(q || []);
        setBillingUsage(usageRes?.values || {});
        setBillingLimits(planRes?.plan?.limits || {});
        setTriggerAuditSummary(triggerSummary || null);
      }).catch(() => {});
    } catch {
      toast.error("Failed to load automations");
    }
  }, [normalizeFlow]);

  useEffect(() => {
    if (!selectedFlowId) return;
    try {
      localStorage.setItem(getFlowSelectionKey(), String(selectedFlowId));
    } catch {
      // ignore storage failures
    }
  }, [selectedFlowId]);

  const loadFlowDetails = useCallback(async (flowId) => {
    if (!flowId) return;
    try {
      let versRaw = await apiGet(`/api/automation/flows/${flowId}/versions`);
      let vers = (versRaw || []).map(normalizeVersion);
      if (!vers.length) {
        const full = await apiGet(`/api/automation/flows/${flowId}`).catch(() => null);
        vers = ((full?.versions || []) || []).map(normalizeVersion);
      }
      setVersions(vers);
      const loadedNodes = parseDefinitionNodes(vers?.[0]?.definitionJson);
      resetNodes(loadedNodes);
      setSelectedNodeId(loadedNodes[0]?.id || "");
      setIsDirty(false);
      setLastSaved(vers?.[0]?.createdAtUtc ? new Date(vers[0].createdAtUtc) : null);
    } catch { toast.error("Failed to load workflow"); }
  }, [normalizeVersion, resetNodes, parseDefinitionNodes]);

  useEffect(() => { loadAll(); }, [loadAll]);
  useEffect(() => { if (selectedFlowId) loadFlowDetails(selectedFlowId); }, [selectedFlowId, loadFlowDetails]);
  useEffect(() => {
    if (!selectedFlow) return;
    setEditFlowForm({
      name: selectedFlow.name || "",
      description: selectedFlow.description || "",
      isActive: !!selectedFlow.isActive,
    });
  }, [selectedFlow]);
  // Mark dirty when nodes change (but not on initial load / reset)
  useEffect(() => {
    if (history.past.length > 0) setIsDirty(true);
  }, [history.past.length]);
  useEffect(() => {
    if (!selectedFlow?.triggerConfigJson) return;
    try {
      const cfg = JSON.parse(selectedFlow.triggerConfigJson);
      if (cfg.keywords?.length) setTriggerKeywords(cfg.keywords.join(","));
    } catch {}
  }, [selectedFlow]);

  const createFlow = async () => {
    if (!canCreateFlow) return toast.error("Flow limit reached. Upgrade your plan.");
    try {
      const def = buildSupportDefinition(createForm.companyName || "your company");
      const payload = {
        name: createForm.name, description: createForm.description,
        channel: "waba", triggerType: "keyword",
        triggerConfigJson: JSON.stringify({ keywords: ["hi", "hello", "HI", "Hello"] }),
        definitionJson: JSON.stringify(def),
      };
      const res = await apiPost("/api/automation/flows", payload);
      toast.success("Bot created successfully 🎉");
      setShowCreate(false);
      setCreateForm({ name: "", description: "", companyName: "" });
      await loadAll();
      const createdFlowId = res?.flow?.id ?? res?.flow?.Id;
      if (createdFlowId) { setSelectedFlowId(String(createdFlowId)); navigate("/dashboard/automations/workflow"); }
    } catch (e) { toast.error(e?.message || "Create failed"); }
  };

  const saveDraft = useCallback(async () => {
    if (!selectedFlowId) return;
    try {
      await apiPost(`/api/automation/flows/${selectedFlowId}/versions`, {
        changeNote: "Saved from flow builder",
        definitionJson: JSON.stringify({
          trigger: { type: "keyword" },
          startNodeId: nodes.find((n) => n.type === "start")?.id || nodes[0]?.id || "",
          nodes, edges,
        }),
      });
      setIsDirty(false);
      setLastSaved(new Date());
      toast.success("Workflow saved ✓");
      await loadFlowDetails(selectedFlowId);
    } catch (e) { toast.error(e?.message || "Save failed"); }
  }, [selectedFlowId, nodes, edges, loadFlowDetails]);

  const saveTriggerKeywords = async () => {
    if (!selectedFlowId || !selectedFlow) return;
    try {
      const keywords = triggerKeywords.split(",").map((x) => x.trim()).filter(Boolean);
      await apiPut(`/api/automation/flows/${selectedFlowId}`, {
        name: selectedFlow.name || "", description: selectedFlow.description || "",
        channel: selectedFlow.channel || "waba", triggerType: selectedFlow.triggerType || "keyword",
        isActive: !!selectedFlow.isActive, triggerConfigJson: JSON.stringify({ keywords }),
      });
      toast.success("Trigger keywords saved");
      await loadAll();
    } catch (e) { toast.error(e?.message || "Failed to save trigger"); }
  };

  const publish = async (flowId = selectedFlowId) => {
    if (!flowId) return;
    const sel = flows.find((x) => String(x.id) === String(flowId));
    if (!canPublishBot && sel?.lifecycleStatus !== "published") return toast.error("Active bot limit reached. Upgrade plan.");
    try {
      if (String(flowId) === String(selectedFlowId)) {
        const unsupported = [...new Set((nodes || [])
          .map((n) => String(n?.type || "").trim().toLowerCase().replace(/-/g, "_"))
          .filter((t) => t && !SUPPORTED_PUBLISH_NODE_TYPES.has(t)))];
        if (unsupported.length) {
          toast.error(`Unsupported node types: ${unsupported.join(", ")}`);
          return;
        }
      }
      const vers = String(flowId) === String(selectedFlowId)
        ? versions
        : ((await apiGet(`/api/automation/flows/${flowId}/versions`)) || []).map(normalizeVersion);
      if (!vers?.length) return toast.error("Save the workflow first");
      await apiPost(`/api/automation/flows/${flowId}/versions/${vers[0].id}/publish`, { requireApproval: false });
      toast.success("Published successfully! 🚀");
      await loadAll();
    } catch (e) { toast.error(e?.message || "Publish failed"); }
  };

  const unpublish = async (flowId = selectedFlowId) => {
    try {
      await apiPost(`/api/automation/flows/${flowId}/unpublish`, {});
      toast.success("Unpublished");
      await loadAll();
    } catch (e) { toast.error(e?.message || "Unpublish failed"); }
  };

  const deleteFlow = async (flowId = selectedFlowId) => {
    if (!flowId || !window.confirm("Delete this bot? This cannot be undone.")) return;
    try {
      await apiDelete(`/api/automation/flows/${flowId}`);
      toast.success("Bot deleted");
      await loadAll();
      if (String(selectedFlowId) === String(flowId)) { setSelectedFlowId(""); setNodes([]); }
    } catch (e) { toast.error(e?.message || "Delete failed"); }
  };

  const updateSelectedFlow = async () => {
    if (!selectedFlowId || !selectedFlow) return;
    try {
      const fallbackKeywords = triggerKeywords.split(",").map((x) => x.trim()).filter(Boolean);
      await apiPut(`/api/automation/flows/${selectedFlowId}`, {
        name: editFlowForm.name || selectedFlow.name || "",
        description: editFlowForm.description || "",
        channel: selectedFlow.channel || "waba",
        triggerType: selectedFlow.triggerType || "keyword",
        isActive: !!editFlowForm.isActive,
        triggerConfigJson: selectedFlow.triggerConfigJson || JSON.stringify({ keywords: fallbackKeywords }),
      });
      toast.success("Flow updated");
      setShowEditFlow(false);
      await loadAll();
    } catch (e) {
      toast.error(e?.message || "Update failed");
    }
  };

  /* ── Node operations ── */
  const updateNode = (id, patch) => setNodes((prev) => prev.map((n) => n.id === id ? { ...n, ...patch } : n));
  const addNode = (type) => {
    // Place new node in visible area with slight randomness
    const x = (120 - pan.x) / zoom + Math.random() * 80;
    const y = (120 - pan.y) / zoom + Math.random() * 80;
    const node = createNode(type, Math.max(80, x), Math.max(80, y));
    setNodes((prev) => [...prev, node]);
    setSelectedNodeId(node.id);
  };
  const removeNode = useCallback((id) => {
    setNodes((prev) => prev.filter((n) => n.id !== id).map((n) => ({
      ...n,
      next: n.next === id ? "" : n.next,
      onTrue: n.onTrue === id ? "" : n.onTrue,
      onFalse: n.onFalse === id ? "" : n.onFalse,
    })));
    if (selectedNodeId === id) setSelectedNodeId("");
  }, [selectedNodeId, setNodes]);

  const duplicateNode = useCallback((id) => {
    const src = nodes.find((n) => n.id === id);
    if (!src) return;
    const copy = { ...src, id: uid(src.type), name: src.name + " (copy)", x: src.x + 40, y: src.y + 40, next: "", onTrue: "", onFalse: "", onFailure: "" };
    setNodes((prev) => [...prev, copy]);
    setSelectedNodeId(copy.id);
    toast.success("Node duplicated");
  }, [nodes, setNodes]);

  const disconnectNode = (id, slot) => {
    updateNode(id, { [slot]: "" });
  };

  /* ── Keyboard shortcuts ── */
  useEffect(() => {
    const handler = (e) => {
      // Don't fire when typing in inputs/textareas
      if (e.target.tagName === "INPUT" || e.target.tagName === "TEXTAREA" || e.target.isContentEditable) return;

      if ((e.ctrlKey || e.metaKey) && e.key === "s") {
        e.preventDefault();
        if (mode === "workflow") saveDraft();
      }
      if ((e.ctrlKey || e.metaKey) && e.key === "z" && !e.shiftKey) {
        e.preventDefault();
        undoNodes();
      }
      if ((e.ctrlKey || e.metaKey) && (e.key === "y" || (e.key === "z" && e.shiftKey))) {
        e.preventDefault();
        redoNodes();
      }
      if ((e.ctrlKey || e.metaKey) && e.key === "d") {
        e.preventDefault();
        if (selectedNodeId) duplicateNode(selectedNodeId);
      }
      if (e.key === "Escape") {
        setSelectedNodeId("");
        setDragConnect(null);
      }
      if ((e.key === "Delete" || e.key === "Backspace") && selectedNodeId) {
        const node = nodes.find((n) => n.id === selectedNodeId);
        if (node && node.type !== "start") removeNode(selectedNodeId);
      }
    };
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, [mode, selectedNodeId, nodes, duplicateNode, removeNode, saveDraft]);


  const canvasToWorld = (cx, cy) => ({
    x: (cx - pan.x) / zoom,
    y: (cy - pan.y) / zoom,
  });

  const onCanvasMouseDown = (e) => {
    const clickedNode = !!e.target.closest("[data-node-id]");
    if (clickedNode) return;
    if (e.target === canvasRef.current || e.target.closest("[data-canvas-bg]")) {
      setIsPanning(true);
      panStart.current = { mx: e.clientX, my: e.clientY, px: pan.x, py: pan.y };
      setSelectedNodeId("");
    }
  };

  const onNodeDragStart = (e, nodeId) => {
    e.preventDefault();
    const node = nodes.find((n) => n.id === nodeId);
    if (!node) return;
    const rect = canvasRef.current.getBoundingClientRect();
    const wx = (e.clientX - rect.left - pan.x) / zoom;
    const wy = (e.clientY - rect.top - pan.y) / zoom;
    dragOffset.current = { x: wx - node.x, y: wy - node.y };
    setDraggingNodeId(nodeId);
  };

  const onStartConnect = (nodeId, e, handleType) => {
    const rect = canvasRef.current.getBoundingClientRect();
    setDragConnect({
      from: nodeId, handleType,
      x2: e.clientX - rect.left,
      y2: e.clientY - rect.top,
    });
  };

  const onConnectToNode = (targetId) => {
    if (!dragConnect || dragConnect.from === targetId) { setDragConnect(null); return; }
    const { from, handleType } = dragConnect;
    const source = nodes.find((n) => n.id === from);
    if (!source) { setDragConnect(null); return; }

    if (handleType === "true") updateNode(from, { onTrue: targetId });
    else if (handleType === "false") updateNode(from, { onFalse: targetId });
    else updateNode(from, { next: targetId });
    setDragConnect(null);
  };

  const onCanvasMouseMove = (e) => {
    if (isPanning && panStart.current) {
      const dx = e.clientX - panStart.current.mx;
      const dy = e.clientY - panStart.current.my;
      setPan({ x: panStart.current.px + dx, y: panStart.current.py + dy });
    }
    if (draggingNodeId) {
      e.preventDefault();
      const rect = canvasRef.current.getBoundingClientRect();
      const wx = (e.clientX - rect.left - pan.x) / zoom;
      const wy = (e.clientY - rect.top - pan.y) / zoom;
      updateNode(draggingNodeId, { x: wx - dragOffset.current.x, y: wy - dragOffset.current.y });
    }
    if (dragConnect) {
      const rect = canvasRef.current.getBoundingClientRect();
      setDragConnect((d) => ({ ...d, x2: e.clientX - rect.left, y2: e.clientY - rect.top }));
    }
  };

  const onCanvasMouseUp = () => {
    setIsPanning(false);
    setDraggingNodeId(null);
    if (dragConnect) setDragConnect(null);
    panStart.current = null;
  };

  useEffect(() => {
    const stopInteractions = () => {
      setIsPanning(false);
      setDraggingNodeId(null);
      panStart.current = null;
    };
    window.addEventListener("mouseup", stopInteractions);
    return () => window.removeEventListener("mouseup", stopInteractions);
  }, []);

  const onWheel = useCallback((e) => {
    e.preventDefault();
    const delta = e.deltaY > 0 ? 0.9 : 1.1;
    setZoom((z) => Math.max(0.3, Math.min(2, z * delta)));
  }, []);

  useEffect(() => {
    const el = canvasRef.current;
    if (!el) return;
    const wheelHandler = (evt) => onWheel(evt);
    el.addEventListener("wheel", wheelHandler, { passive: false });
    return () => el.removeEventListener("wheel", wheelHandler);
  }, [onWheel]);

  const fitToScreen = () => {
    if (!nodes.length) return;
    const xs = nodes.map((n) => n.x);
    const ys = nodes.map((n) => n.y);
    const minX = Math.min(...xs) - 40;
    const minY = Math.min(...ys) - 40;
    const maxX = Math.max(...xs) + NODE_W + 40;
    const maxY = Math.max(...ys) + NODE_H_BASE + 80;
    const rect = canvasRef.current?.getBoundingClientRect();
    if (!rect) return;
    const zx = rect.width / (maxX - minX);
    const zy = rect.height / (maxY - minY);
    const newZoom = Math.max(0.3, Math.min(1.2, Math.min(zx, zy) * 0.9));
    setZoom(newZoom);
    setPan({ x: -minX * newZoom + 40, y: -minY * newZoom + 40 });
  };

  /* ── FAQ ── */
  const saveFaq = async () => {
    try {
      if (!faqForm.question.trim() || !faqForm.answer.trim()) return toast.error("Question and answer required");
      if (editingFaqId) {
        await apiPut(`/api/automation/faq/${editingFaqId}`, faqForm);
        toast.success("Q&A entry updated");
        setEditingFaqId(null);
      } else {
        await apiPost("/api/automation/faq", faqForm);
        toast.success("Q&A entry added");
      }
      setFaqForm({ question: "", answer: "", category: "", isActive: true });
      await loadAll();
    } catch (e) { toast.error(e?.message || "Failed"); }
  };

  const startEditFaq = (item) => {
    setEditingFaqId(item.id);
    setFaqForm({ question: item.question, answer: item.answer, category: item.category || "", isActive: item.isActive ?? true });
  };

  const cancelEditFaq = () => {
    setEditingFaqId(null);
    setFaqForm({ question: "", answer: "", category: "", isActive: true });
  };

  const deleteFaq = async (id) => {
    try { await apiDelete(`/api/automation/faq/${id}`); await loadAll(); }
    catch (e) { toast.error(e?.message || "Delete failed"); }
  };

  /* ── Filtered node library ── */
  const filteredSections = NODE_SECTIONS.map((s) => ({
    ...s,
    items: s.items.filter((type) => {
      const meta = NODE_META[type];
      if (!searchQuery) return true;
      return meta?.label.toLowerCase().includes(searchQuery.toLowerCase()) || meta?.hint.toLowerCase().includes(searchQuery.toLowerCase());
    }),
  })).filter((s) => s.items.length > 0);

  /* ─── TAB BAR ────────────────────────────────────────────────────────── */
  const tabs = [
    { key: "overview", label: "Automations", href: "/dashboard/automations", icon: Layers },
    { key: "workflow", label: "Workflow Builder", href: "/dashboard/automations/workflow", icon: GitBranch },
    { key: "qa", label: "Q&A Knowledge Base", href: "/dashboard/automations/qa", icon: HelpCircle },
  ];

  /* ═════════════════════════════════════════════════════════════════════════
     RENDER
  ═════════════════════════════════════════════════════════════════════════ */
  return (
    <TooltipProvider>
      <div className="flex flex-col h-screen bg-slate-50 overflow-hidden">
        {/* ── Top nav ── */}
        <div className="flex-shrink-0 border-b border-slate-200 bg-white">
          <div className="flex items-center gap-1 px-4 pt-0">
            {tabs.map((t) => {
              const TIcon = t.icon;
              const active = mode === t.key;
              return (
                <Link key={t.key} to={t.href}
                  className={`flex items-center gap-1.5 px-3 py-3 text-sm font-medium border-b-2 transition-colors whitespace-nowrap ${
                    active
                      ? "border-orange-500 text-orange-600"
                      : "border-transparent text-slate-500 hover:text-slate-700 hover:border-slate-300"
                  }`}>
                  <TIcon size={14} />{t.label}
                </Link>
              );
            })}
          </div>
        </div>

        {/* ── Page content ── */}
        <div className="flex-1 overflow-hidden">
          {mode === "overview" && <OverviewPage flows={flows} limits={limits} flowUsed={flowUsed} flowLimit={flowLimit} activeBots={activeBots} chatbotLimit={chatbotLimit} canCreateFlow={canCreateFlow} canPublishBot={canPublishBot} showCreate={showCreate} setShowCreate={setShowCreate} createForm={createForm} setCreateForm={setCreateForm} createFlow={createFlow} setSelectedFlowId={setSelectedFlowId} navigate={navigate} publish={publish} unpublish={unpublish} deleteFlow={deleteFlow} triggerAuditSummary={triggerAuditSummary} />}
          {mode === "workflow" && <WorkflowCanvas
            flows={flows} selectedFlowId={selectedFlowId} setSelectedFlowId={setSelectedFlowId}
            selectedFlow={selectedFlow} nodes={nodes} edges={edges} selectedNodeId={selectedNodeId}
            selectedFlowStatus={selectedFlowStatus}
            setSelectedNodeId={setSelectedNodeId} selectedNode={selectedNode} zoom={zoom} pan={pan}
            draggingNodeId={draggingNodeId} dragConnect={dragConnect} canvasRef={canvasRef}
            filteredSections={filteredSections} searchQuery={searchQuery} setSearchQuery={setSearchQuery}
            activeSection={activeSection} setActiveSection={setActiveSection}
            showPreview={showPreview} setShowPreview={setShowPreview}
            showVersions={showVersions} setShowVersions={setShowVersions}
            versions={versions}
            outside24h={outside24h} setOutside24h={setOutside24h}
            triggerKeywords={triggerKeywords} setTriggerKeywords={setTriggerKeywords}
            onWheel={onWheel} onCanvasMouseDown={onCanvasMouseDown} onCanvasMouseMove={onCanvasMouseMove}
            onCanvasMouseUp={onCanvasMouseUp} onNodeDragStart={onNodeDragStart} onStartConnect={onStartConnect}
            onConnectToNode={onConnectToNode} addNode={addNode} updateNode={updateNode} removeNode={removeNode}
            duplicateNode={duplicateNode} disconnectNode={disconnectNode}
            saveDraft={saveDraft} publish={publish} saveTriggerKeywords={saveTriggerKeywords}
            setZoom={setZoom} fitToScreen={fitToScreen} canPublishBot={canPublishBot}
            canUndo={canUndo} canRedo={canRedo} undoNodes={undoNodes} redoNodes={redoNodes}
            isDirty={isDirty} lastSaved={lastSaved} validation={validation}
            resetNodes={resetNodes}
            loadAll={loadAll}
            loadFlowDetails={loadFlowDetails}
            showEditFlow={showEditFlow} setShowEditFlow={setShowEditFlow}
            editFlowForm={editFlowForm} setEditFlowForm={setEditFlowForm}
            updateSelectedFlow={updateSelectedFlow} deleteFlow={deleteFlow}
          />}
          {mode === "qa" && <QaPage faqItems={faqItems} faqForm={faqForm} setFaqForm={setFaqForm} saveFaq={saveFaq} deleteFaq={deleteFaq} editingFaqId={editingFaqId} startEditFaq={startEditFaq} cancelEditFaq={cancelEditFaq} />}
        </div>
      </div>
    </TooltipProvider>
  );
}

/* ═══════════════════════════════════════════════════════════════════════════════
   OVERVIEW PAGE
═══════════════════════════════════════════════════════════════════════════════ */
function OverviewPage({ flows, limits, flowUsed, flowLimit, activeBots, chatbotLimit, canCreateFlow, canPublishBot, showCreate, setShowCreate, createForm, setCreateForm, createFlow, setSelectedFlowId, navigate, publish, unpublish, deleteFlow, triggerAuditSummary }) {
  return (
    <div className="p-6 space-y-6 overflow-auto h-full">
      {/* Stats */}
      <div className="grid grid-cols-1 md:grid-cols-4 gap-4">
        {[
          { label: "Total Bots", value: flows.length, sub: `${flowUsed} / ${flowLimit > 0 ? flowLimit : "∞"} used`, icon: Bot, color: T.orange },
          { label: "Active Bots", value: flows.filter((x) => x.lifecycleStatus === "published").length, sub: `${activeBots} / ${chatbotLimit > 0 ? chatbotLimit : "∞"} active`, icon: Activity, color: T.success },
          { label: "Runs Today", value: limits?.usage?.runsToday ?? 0, sub: "Automated conversations", icon: BarChart3, color: T.info },
          { label: "Q&A Entries", value: limits?.usage?.faqCount ?? "—", sub: "Knowledge base items", icon: HelpCircle, color: "#8b5cf6" },
        ].map((stat) => (
          <div key={stat.label} className="bg-white rounded-2xl border border-slate-200 p-5 flex items-start gap-4">
            <div className="w-10 h-10 rounded-xl flex items-center justify-center flex-shrink-0" style={{ background: stat.color + "15" }}>
              <stat.icon size={18} style={{ color: stat.color }} />
            </div>
            <div>
              <div className="text-2xl font-bold text-slate-900">{stat.value}</div>
              <div className="text-xs font-semibold text-slate-700">{stat.label}</div>
              <div className="text-xs text-slate-400 mt-0.5">{stat.sub}</div>
            </div>
          </div>
        ))}
      </div>

      {/* Trigger Evaluation Summary */}
      <div className="bg-white rounded-2xl border border-slate-200 p-5">
        <div className="flex items-center justify-between mb-3">
          <h3 className="text-sm font-semibold text-slate-800">Trigger Health (Last 7 days)</h3>
          <Badge variant="outline" className="text-xs">
            Match Rate {Number(triggerAuditSummary?.matchRate ?? 0).toFixed(1)}%
          </Badge>
        </div>
        <div className="grid grid-cols-1 md:grid-cols-3 gap-3 mb-3">
          <div className="rounded-xl border border-slate-200 p-3">
            <div className="text-xs text-slate-500">Total Evaluations</div>
            <div className="text-xl font-semibold text-slate-900">{triggerAuditSummary?.total ?? 0}</div>
          </div>
          <div className="rounded-xl border border-emerald-200 bg-emerald-50/40 p-3">
            <div className="text-xs text-emerald-700">Matched</div>
            <div className="text-xl font-semibold text-emerald-700">{triggerAuditSummary?.matched ?? 0}</div>
          </div>
          <div className="rounded-xl border border-amber-200 bg-amber-50/40 p-3">
            <div className="text-xs text-amber-700">Unmatched</div>
            <div className="text-xl font-semibold text-amber-700">{triggerAuditSummary?.unmatched ?? 0}</div>
          </div>
        </div>
        {!!triggerAuditSummary?.reasons?.length && (
          <div className="rounded-xl border border-slate-200 overflow-hidden">
            <div className="px-3 py-2 text-xs font-semibold text-slate-600 bg-slate-50">Top Reasons</div>
            <div className="divide-y divide-slate-100">
              {triggerAuditSummary.reasons.slice(0, 6).map((r) => (
                <div key={r.reason} className="px-3 py-2 text-xs flex items-center justify-between">
                  <span className="text-slate-600 truncate">{r.reason}</span>
                  <span className="font-semibold text-slate-800">{r.count}</span>
                </div>
              ))}
            </div>
          </div>
        )}
      </div>

      {/* Bot List */}
      <div className="bg-white rounded-2xl border border-slate-200 overflow-hidden">
        <div className="flex items-center justify-between px-6 py-4 border-b border-slate-100">
          <h2 className="font-semibold text-slate-800">Automation Bots</h2>
          <Dialog open={showCreate} onOpenChange={setShowCreate}>
            <DialogTrigger asChild>
              <Button className="text-white gap-2" style={{ background: T.orange }} disabled={!canCreateFlow}>
                <Plus size={15} />Create Bot
              </Button>
            </DialogTrigger>
            <DialogContent className="sm:max-w-md">
              <DialogHeader>
                <DialogTitle>Create New Bot</DialogTitle>
                <DialogDescription>A professional WhatsApp chatbot will be created with a starter workflow.</DialogDescription>
              </DialogHeader>
              <div className="space-y-4 pt-2">
                <div><Label className="text-xs font-semibold">Bot Name *</Label><Input className="mt-1" value={createForm.name} onChange={(e) => setCreateForm((p) => ({ ...p, name: e.target.value }))} placeholder="Customer Support Bot" /></div>
                <div><Label className="text-xs font-semibold">Company Name</Label><Input className="mt-1" value={createForm.companyName} onChange={(e) => setCreateForm((p) => ({ ...p, companyName: e.target.value }))} placeholder="Acme Inc" /></div>
                <div><Label className="text-xs font-semibold">Description</Label><Textarea className="mt-1" rows={2} value={createForm.description} onChange={(e) => setCreateForm((p) => ({ ...p, description: e.target.value }))} /></div>
                <Button className="w-full text-white" style={{ background: T.orange }} onClick={createFlow} disabled={!createForm.name.trim()}>Create Bot</Button>
              </div>
            </DialogContent>
          </Dialog>
        </div>

        {flows.length === 0 ? (
          <div className="text-center py-16 text-slate-400">
            <Bot size={40} className="mx-auto mb-3 opacity-30" />
            <div className="font-medium">No bots yet</div>
            <div className="text-sm mt-1">Create your first automation bot to get started</div>
          </div>
        ) : (
          <div className="divide-y divide-slate-100">
            {flows.map((f) => (
              <div
                key={f.id}
                className="flex items-center gap-4 px-6 py-4 hover:bg-slate-50 transition-colors cursor-pointer"
                onClick={() => { setSelectedFlowId(String(f.id)); navigate("/dashboard/automations/workflow"); }}
              >
                <div className="w-9 h-9 rounded-xl flex items-center justify-center flex-shrink-0" style={{ background: T.orangeLight }}>
                  <Bot size={16} style={{ color: T.orange }} />
                </div>
                <div className="flex-1 min-w-0">
                  <div className="font-semibold text-slate-800">{f.name}</div>
                  <div className="text-xs text-slate-400 mt-0.5">{f.description || "—"} · Updated {f.updatedAtUtc ? new Date(f.updatedAtUtc).toLocaleDateString() : "—"}</div>
                </div>
                <Badge className={`${FLOW_COLORS[f.lifecycleStatus]} border text-xs font-medium`}>{f.lifecycleStatus}</Badge>
                <div className="flex items-center gap-1.5">
                  <Button size="sm" variant="outline" className="text-xs h-7 gap-1"
                    onClick={(e) => { e.stopPropagation(); setSelectedFlowId(String(f.id)); navigate("/dashboard/automations/workflow"); }}>
                    <Settings2 size={11} />Edit
                  </Button>
                  <Button size="sm" variant="outline" className="text-xs h-7 gap-1"
                    onClick={(e) => { e.stopPropagation(); setSelectedFlowId(String(f.id)); navigate("/dashboard/automations/workflow"); }}>
                    <GitBranch size={11} />Workflow
                  </Button>
                  <Button size="sm" className="text-xs h-7 text-white gap-1" style={{ background: T.orange }}
                    onClick={(e) => { e.stopPropagation(); publish(f.id); }} disabled={!canPublishBot && f.lifecycleStatus !== "published"}>
                    <UploadCloud size={11} />Publish
                  </Button>
                  {f.lifecycleStatus === "published" && (
                    <Button size="sm" variant="outline" className="text-xs h-7" onClick={(e) => { e.stopPropagation(); unpublish(f.id); }}>Unpublish</Button>
                  )}
                  <Button size="sm" variant="ghost" className="text-xs h-7 text-red-500 hover:bg-red-50" onClick={(e) => { e.stopPropagation(); deleteFlow(f.id); }}>
                    <Trash2 size={11} />
                  </Button>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}

/* ═══════════════════════════════════════════════════════════════════════════════
   WORKFLOW CANVAS PAGE
═══════════════════════════════════════════════════════════════════════════════ */
function WorkflowCanvas({
  flows, selectedFlowId, setSelectedFlowId, selectedFlow, nodes, edges, selectedNodeId, setSelectedNodeId,
  selectedFlowStatus,
  selectedNode, zoom, pan, draggingNodeId, dragConnect, canvasRef, filteredSections, searchQuery, setSearchQuery,
  activeSection, setActiveSection, showPreview, setShowPreview, showVersions, setShowVersions, versions,
  outside24h, setOutside24h, triggerKeywords, setTriggerKeywords,
  onWheel, onCanvasMouseDown, onCanvasMouseMove, onCanvasMouseUp,
  onNodeDragStart, onStartConnect, onConnectToNode, addNode, updateNode, removeNode,
  duplicateNode, disconnectNode,
  saveDraft, publish, saveTriggerKeywords, setZoom, fitToScreen, canPublishBot,
  canUndo, canRedo, undoNodes, redoNodes, isDirty, lastSaved, validation, resetNodes,
  loadAll, loadFlowDetails,
  showEditFlow, setShowEditFlow, editFlowForm, setEditFlowForm, updateSelectedFlow, deleteFlow
}) {
  const [showPaletteDrawer, setShowPaletteDrawer] = useState(false);
  const [showInspectorDrawer, setShowInspectorDrawer] = useState(false);
  const [showImportMetaDialog, setShowImportMetaDialog] = useState(false);
  const [metaFlowBusy, setMetaFlowBusy] = useState(false);
  const [metaFlowImportBusy, setMetaFlowImportBusy] = useState(false);
  const [metaFlowActionBusy, setMetaFlowActionBusy] = useState(false);
  const [metaFlows, setMetaFlows] = useState([]);
  const [selectedMetaFlowId, setSelectedMetaFlowId] = useState("");
  const [newMetaFlowName, setNewMetaFlowName] = useState("");
  const [renameMetaFlowName, setRenameMetaFlowName] = useState("");

  const [showSendFlowDialog, setShowSendFlowDialog] = useState(false);
  const [sendFlowBusy, setSendFlowBusy] = useState(false);
  const [sendFlowForm, setSendFlowForm] = useState({
    recipient: "",
    body: "Please continue to next step.",
    flowRef: "",
    cta: "Open",
    screen: "",
    dataJson: "{}",
  });
  const [showExchangeDialog, setShowExchangeDialog] = useState(false);
  const [exchangeBusy, setExchangeBusy] = useState(false);
  const [exchangeForm, setExchangeForm] = useState({
    action: "customer_lookup",
    screenId: "",
    payloadJson: "{}",
  });
  const currentDefinitionJson = useMemo(() => JSON.stringify({
    trigger: { type: "keyword" },
    startNodeId: nodes.find((n) => n.type === "start")?.id || nodes[0]?.id || "",
    nodes, edges,
  }), [nodes, edges]);

  const validateServerSchema = async () => {
    try {
      const report = await apiPost("/api/automation/flows/validate-definition", { definitionJson: currentDefinitionJson });
      if (report?.isValid) toast.success("Flow schema valid");
      else toast.error(`Schema errors: ${(report?.errors || []).length || 0}`);
    } catch (e) {
      toast.error(e?.message || "Schema validation failed");
    }
  };

  const sendFlowMessage = async () => {
    if (!selectedFlowId) return;
    if (!sendFlowForm.recipient.trim()) return toast.error("Recipient is required");
    setSendFlowBusy(true);
    try {
      await apiPost(`/api/automation/flows/${selectedFlowId}/send-flow`, {
        recipient: sendFlowForm.recipient.trim(),
        body: sendFlowForm.body,
        flowId: sendFlowForm.flowRef.trim(),
        flowCta: sendFlowForm.cta.trim(),
        flowScreen: sendFlowForm.screen.trim(),
        flowDataJson: sendFlowForm.dataJson,
      });
      toast.success("Flow sent to user");
      setShowSendFlowDialog(false);
    } catch (e) {
      toast.error(e?.message || "Failed to send flow");
    } finally {
      setSendFlowBusy(false);
    }
  };

  const runDataExchange = async () => {
    if (!selectedFlowId) return;
    if (!exchangeForm.action.trim()) return toast.error("Action is required");
    setExchangeBusy(true);
    try {
      const out = await apiPost(`/api/automation/flows/${selectedFlowId}/data-exchange`, {
        action: exchangeForm.action.trim(),
        screenId: exchangeForm.screenId.trim(),
        payloadJson: exchangeForm.payloadJson,
      });
      toast.success(`Data exchange: HTTP ${out?.statusCode || 200}`);
      setShowExchangeDialog(false);
    } catch (e) {
      toast.error(e?.message || "Data exchange failed");
    } finally {
      setExchangeBusy(false);
    }
  };

  const openMetaImport = async () => {
    setShowImportMetaDialog(true);
    setMetaFlowBusy(true);
    try {
      const res = await apiGet("/api/automation/meta/flows");
      const items = Array.isArray(res?.data) ? res.data : [];
      setMetaFlows(items);
      if (items.length) {
        const id = String(items[0]?.id || "");
        setSelectedMetaFlowId(id);
        setRenameMetaFlowName(items[0]?.name || "");
      }
    } catch (e) {
      toast.error(e?.message || "Failed to load Meta flows");
    } finally {
      setMetaFlowBusy(false);
    }
  };

  const importFromMeta = async () => {
    if (!selectedFlowId) return;
    if (!selectedMetaFlowId) return toast.error("Select a Meta flow first");
    setMetaFlowImportBusy(true);
    try {
      const out = await apiPost(`/api/automation/flows/${selectedFlowId}/import-meta`, {
        metaFlowId: selectedMetaFlowId,
        createNewVersion: true,
        changeNote: "Imported from Meta"
      });
      await loadFlowDetails(selectedFlowId);
      await loadAll();
      setShowImportMetaDialog(false);
      toast.success(out?.warning ? `Imported with warning: ${out.warning}` : "Meta flow imported");
    } catch (e) {
      toast.error(e?.message || "Meta flow import failed");
    } finally {
      setMetaFlowImportBusy(false);
    }
  };

  const onMetaSelect = (id) => {
    setSelectedMetaFlowId(id);
    const found = metaFlows.find((x) => String(x.id) === String(id));
    setRenameMetaFlowName(found?.name || "");
  };

  const createMetaFlow = async () => {
    if (!newMetaFlowName.trim()) return toast.error("Meta flow name is required");
    setMetaFlowActionBusy(true);
    try {
      await apiPost("/api/automation/meta/flows", {
        name: newMetaFlowName.trim(),
        categoriesJson: "[\"OTHER\"]",
        jsonVersion: "3.0",
        flowJson: JSON.stringify({
          version: "3.0",
          screens: [{ id: "welcome", title: newMetaFlowName.trim(), components: [] }],
          routing: { startScreen: "welcome", edges: [] }
        })
      });
      toast.success("Meta flow created");
      setNewMetaFlowName("");
      await openMetaImport();
    } catch (e) {
      toast.error(e?.message || "Create meta flow failed");
    } finally {
      setMetaFlowActionBusy(false);
    }
  };

  const updateMetaFlowName = async () => {
    if (!selectedMetaFlowId) return;
    if (!renameMetaFlowName.trim()) return toast.error("Flow name is required");
    setMetaFlowActionBusy(true);
    try {
      await apiPut(`/api/automation/meta/flows/${selectedMetaFlowId}`, { name: renameMetaFlowName.trim() });
      toast.success("Meta flow updated");
      await openMetaImport();
    } catch (e) {
      toast.error(e?.message || "Update meta flow failed");
    } finally {
      setMetaFlowActionBusy(false);
    }
  };

  const publishMetaFlow = async () => {
    if (!selectedMetaFlowId) return;
    setMetaFlowActionBusy(true);
    try {
      await apiPost(`/api/automation/meta/flows/${selectedMetaFlowId}/publish`, { flowJson: "" });
      toast.success("Meta flow published");
      await openMetaImport();
    } catch (e) {
      toast.error(e?.message || "Publish meta flow failed");
    } finally {
      setMetaFlowActionBusy(false);
    }
  };

  const deleteMetaFlow = async () => {
    if (!selectedMetaFlowId) return;
    if (!window.confirm("Delete selected Meta flow?")) return;
    setMetaFlowActionBusy(true);
    try {
      await apiDelete(`/api/automation/meta/flows/${selectedMetaFlowId}`);
      toast.success("Meta flow deleted");
      setSelectedMetaFlowId("");
      setRenameMetaFlowName("");
      await openMetaImport();
    } catch (e) {
      toast.error(e?.message || "Delete meta flow failed");
    } finally {
      setMetaFlowActionBusy(false);
    }
  };

  if (!selectedFlowId) {
    return (
      <div className="flex items-center justify-center h-full">
        <div className="text-center space-y-3">
          <GitBranch size={48} className="mx-auto text-slate-300" />
          <div className="text-slate-500 font-medium">No workflow selected</div>
          <div className="text-sm text-slate-400">Go to Automations and click <strong>Workflow</strong> on a bot</div>
        </div>
      </div>
    );
  }

  return (
    <div className="relative flex h-full min-w-0 overflow-hidden">
      {/* ── Left palette ── */}
      <div className="hidden xl:flex w-44 2xl:w-56 flex-shrink-0 bg-white border-r border-slate-200 flex-col min-w-0">
        {/* Search */}
        <div className="p-3 border-b border-slate-100">
          <div className="relative">
            <Search size={13} className="absolute left-2.5 top-2.5 text-slate-400" />
            <Input value={searchQuery} onChange={(e) => setSearchQuery(e.target.value)} className="pl-8 h-8 text-xs" placeholder="Search nodes..." />
          </div>
        </div>

        {/* Trigger config (moved to top for visibility) */}
        <div className="border-b border-slate-100 p-3 space-y-2">
          <Label className="text-[10px] font-bold uppercase tracking-wide text-slate-400 flex items-center gap-1"><Zap size={10} />Trigger Keywords</Label>
          <Input value={triggerKeywords} onChange={(e) => setTriggerKeywords(e.target.value)} className="h-7 text-xs" placeholder="hi,hello,support" />
          <Button size="sm" className="w-full h-7 text-xs text-white" style={{ background: T.orange }} onClick={saveTriggerKeywords}>Save Trigger</Button>
        </div>

        <ScrollArea className="flex-1">
          <div className="p-3 space-y-3">
            {filteredSections.map((section) => {
              const SIcon = section.icon;
              const isOpen = activeSection === section.id || searchQuery;
              return (
                <div key={section.id}>
                  <button
                    className="w-full flex items-center justify-between py-1.5 px-2 rounded-lg text-xs font-bold uppercase tracking-wide hover:bg-slate-50 transition-colors"
                    style={{ color: section.color }}
                    onClick={() => setActiveSection(isOpen && !searchQuery ? null : section.id)}
                  >
                    <div className="flex items-center gap-1.5"><SIcon size={11} />{section.label}</div>
                    {!searchQuery && (isOpen ? <ChevronDown size={11} /> : <ChevronRight size={11} />)}
                  </button>

                  {(isOpen) && (
                    <div className="mt-1.5 space-y-1">
                      {section.items.map((type) => {
                        const meta = NODE_META[type];
                        const MIcon = meta?.icon || MessageCircle;
                        return (
                          <Tooltip key={type}>
                            <TooltipTrigger asChild>
                              <button
                                className="w-full flex items-center gap-2.5 rounded-lg px-2.5 py-2 text-left hover:shadow-sm transition-all cursor-grab active:cursor-grabbing"
                                style={{ background: meta?.bg, border: `1px solid ${meta?.border}` }}
                                onClick={() => addNode(type)}
                              >
                                <div className="w-6 h-6 rounded-md flex items-center justify-center flex-shrink-0" style={{ background: meta?.color }}>
                                  <MIcon size={11} color="white" />
                                </div>
                                <div className="min-w-0">
                                  <div className="text-[11px] font-semibold text-slate-800 leading-tight">{meta?.label}</div>
                                  <div className="text-[10px] text-slate-400 leading-tight truncate">{meta?.hint}</div>
                                </div>
                              </button>
                            </TooltipTrigger>
                            <TooltipContent side="right" className="text-xs">{meta?.hint}</TooltipContent>
                          </Tooltip>
                        );
                      })}
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        </ScrollArea>
      </div>

      {/* ── Canvas ── */}
      <div className="flex-1 flex flex-col min-w-0">
        {/* Toolbar */}
        <div className="min-h-12 flex-shrink-0 bg-white border-b border-slate-200 flex flex-wrap items-center justify-between px-4 py-2 gap-3">
          <div className="flex items-center gap-2 min-w-0 flex-wrap">
            <Select value={selectedFlowId} onValueChange={setSelectedFlowId}>
              <SelectTrigger className="h-7 text-xs w-44"><SelectValue /></SelectTrigger>
              <SelectContent>{flows.map((f) => <SelectItem key={f.id} value={String(f.id)} className="text-xs">{f.name}</SelectItem>)}</SelectContent>
            </Select>
            {selectedFlow && (
              <Badge className={`${FLOW_COLORS[selectedFlowStatus] || FLOW_COLORS.draft} border text-xs`}>{selectedFlowStatus}</Badge>
            )}
            {/* Dirty indicator */}
            {isDirty && <span className="text-[10px] text-amber-500 font-medium flex items-center gap-1"><div className="w-1.5 h-1.5 rounded-full bg-amber-400" />Unsaved</span>}
            {!isDirty && lastSaved && <span className="text-[10px] text-slate-400">Saved {lastSaved.toLocaleTimeString()}</span>}
          </div>

          <div className="flex items-center gap-1.5 flex-wrap justify-end min-w-0">
            <Button variant="outline" size="sm" className="h-7 text-xs gap-1 xl:hidden" onClick={() => setShowPaletteDrawer(true)}>
              <Layers size={12} />Nodes
            </Button>
            <Button variant="outline" size="sm" className="h-7 text-xs gap-1 2xl:hidden" onClick={() => setShowInspectorDrawer(true)}>
              <Settings2 size={12} />Inspector
            </Button>
            {/* Validation */}
            {validation.errors.length > 0 && (
              <Tooltip>
                <TooltipTrigger asChild>
                  <div className="flex items-center gap-1 text-[11px] font-medium px-2 py-1 rounded-lg" style={{ background: "#fef2f2", color: T.error }}>
                    <AlertTriangle size={11} />{validation.errors.length} error{validation.errors.length > 1 ? "s" : ""}
                  </div>
                </TooltipTrigger>
                <TooltipContent side="bottom" className="text-xs max-w-xs">
                  <div className="space-y-1">{validation.errors.map((e, i) => <div key={i}>• {e}</div>)}</div>
                </TooltipContent>
              </Tooltip>
            )}
            {validation.warnings.length > 0 && validation.errors.length === 0 && (
              <Tooltip>
                <TooltipTrigger asChild>
                  <div className="flex items-center gap-1 text-[11px] font-medium px-2 py-1 rounded-lg" style={{ background: "#fffbeb", color: T.warning }}>
                    <AlertTriangle size={11} />{validation.warnings.length} warning{validation.warnings.length > 1 ? "s" : ""}
                  </div>
                </TooltipTrigger>
                <TooltipContent side="bottom" className="text-xs max-w-xs">
                  <div className="space-y-1">{validation.warnings.map((w, i) => <div key={i}>• {w}</div>)}</div>
                </TooltipContent>
              </Tooltip>
            )}
            {validation.errors.length === 0 && validation.warnings.length === 0 && nodes.length > 0 && (
              <div className="flex items-center gap-1 text-[11px] font-medium px-2 py-1 rounded-lg" style={{ background: "#f0fdf4", color: T.success }}>
                <CheckCircle2 size={11} />Valid
              </div>
            )}

            <div className="w-px h-5 bg-slate-200 hidden xl:block" />

            {/* Undo / Redo */}
            <Tooltip>
              <TooltipTrigger asChild>
                <button className="px-2 py-1.5 border border-slate-200 rounded-lg hover:bg-slate-50 text-slate-500 transition-colors disabled:opacity-30"
                  onClick={undoNodes} disabled={!canUndo}>
                  <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M9 14 4 9l5-5"/><path d="M4 9h10.5a5.5 5.5 0 0 1 5.5 5.5v0a5.5 5.5 0 0 1-5.5 5.5H11"/></svg>
                </button>
              </TooltipTrigger>
              <TooltipContent className="text-xs">Undo (Ctrl+Z)</TooltipContent>
            </Tooltip>
            <Tooltip>
              <TooltipTrigger asChild>
                <button className="px-2 py-1.5 border border-slate-200 rounded-lg hover:bg-slate-50 text-slate-500 transition-colors disabled:opacity-30"
                  onClick={redoNodes} disabled={!canRedo}>
                  <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="m15 14 5-5-5-5"/><path d="M20 9H9.5A5.5 5.5 0 0 0 4 14.5v0A5.5 5.5 0 0 0 9.5 20H13"/></svg>
                </button>
              </TooltipTrigger>
              <TooltipContent className="text-xs">Redo (Ctrl+Y)</TooltipContent>
            </Tooltip>

            <div className="w-px h-5 bg-slate-200 hidden xl:block" />

            {/* Zoom controls */}
            <div className="flex items-center border border-slate-200 rounded-lg overflow-hidden">
              <button className="px-2 py-1.5 hover:bg-slate-50 text-slate-500 transition-colors" onClick={() => setZoom((z) => Math.max(0.3, z * 0.8))}><ZoomOut size={13} /></button>
              <span className="px-2 text-xs text-slate-600 font-mono">{Math.round(zoom * 100)}%</span>
              <button className="px-2 py-1.5 hover:bg-slate-50 text-slate-500 transition-colors" onClick={() => setZoom((z) => Math.min(2, z * 1.2))}><ZoomIn size={13} /></button>
            </div>
            <Tooltip>
              <TooltipTrigger asChild>
                <button className="px-2 py-1.5 border border-slate-200 rounded-lg hover:bg-slate-50 text-slate-500 transition-colors" onClick={fitToScreen}><Maximize2 size={13} /></button>
              </TooltipTrigger>
              <TooltipContent className="text-xs">Fit to screen</TooltipContent>
            </Tooltip>

            <div className="w-px h-5 bg-slate-200 hidden xl:block" />

            {/* Version history toggle */}
            <Tooltip>
              <TooltipTrigger asChild>
                <button
                  className={`px-2 py-1.5 border rounded-lg text-slate-500 transition-colors ${showVersions ? "border-orange-300 bg-orange-50 text-orange-600" : "border-slate-200 hover:bg-slate-50"}`}
                  onClick={() => setShowVersions((v) => !v)}>
                  <Clock size={13} />
                </button>
              </TooltipTrigger>
              <TooltipContent className="text-xs">Version History</TooltipContent>
            </Tooltip>

            <label className="flex items-center gap-1.5 text-xs text-slate-600 cursor-pointer">
              <input type="checkbox" checked={showPreview} onChange={(e) => setShowPreview(e.target.checked)} className="rounded" />
              Panel
            </label>
            <label className="flex items-center gap-1.5 text-xs text-slate-600 cursor-pointer">
              <input type="checkbox" checked={outside24h} onChange={(e) => setOutside24h(e.target.checked)} className="rounded" />
              24h+
            </label>

            <div className="w-px h-5 bg-slate-200 hidden xl:block" />

            <Button size="sm" className="h-7 text-xs gap-1 text-white" style={{ background: T.orange }} onClick={saveDraft}>
              <Save size={12} />Save Flow
              <kbd className="ml-1 text-[9px] bg-white/20 rounded px-1 text-white/90">Ctrl+S</kbd>
            </Button>
            <Button variant="outline" size="sm" className="h-7 text-xs gap-1" onClick={validateServerSchema}>
              <CheckSquare size={12} />Validate Schema
            </Button>
            <Dialog open={showImportMetaDialog} onOpenChange={setShowImportMetaDialog}>
              <DialogTrigger asChild>
                <Button variant="outline" size="sm" className="h-7 text-xs gap-1" onClick={openMetaImport}>
                  <UploadCloud size={12} />Import Meta
                </Button>
              </DialogTrigger>
              <DialogContent className="sm:max-w-md">
                <DialogHeader>
                  <DialogTitle>Import From Meta Flow Builder</DialogTitle>
                  <DialogDescription>Keep Textzy UI components and map Meta screens/routing into this workflow.</DialogDescription>
                </DialogHeader>
                <div className="space-y-3 pt-2">
                  {metaFlowBusy ? (
                    <div className="text-sm text-slate-500">Loading Meta flows...</div>
                  ) : (
                    <>
                      <div>
                        <Label className="text-xs font-semibold">Meta Flow</Label>
                        <Select value={selectedMetaFlowId} onValueChange={onMetaSelect}>
                          <SelectTrigger className="mt-1 h-9 text-xs">
                            <SelectValue placeholder="Select Meta flow" />
                          </SelectTrigger>
                          <SelectContent>
                            {metaFlows.map((f) => (
                              <SelectItem key={f.id} value={String(f.id)} className="text-xs">
                                {f.name || f.id} {f.status ? `(${f.status})` : ""}
                              </SelectItem>
                            ))}
                          </SelectContent>
                        </Select>
                      </div>
                      <div className="grid grid-cols-2 gap-2">
                        <div className="col-span-2">
                          <Label className="text-xs font-semibold">Create Meta Flow</Label>
                          <Input className="mt-1" value={newMetaFlowName} onChange={(e) => setNewMetaFlowName(e.target.value)} placeholder="New flow name" />
                        </div>
                        <Button variant="outline" onClick={createMetaFlow} disabled={metaFlowActionBusy} className="text-xs">
                          Create
                        </Button>
                        <Button variant="outline" onClick={openMetaImport} disabled={metaFlowBusy || metaFlowActionBusy} className="text-xs">
                          Refresh
                        </Button>
                      </div>
                      <div className="grid grid-cols-2 gap-2">
                        <div className="col-span-2">
                          <Label className="text-xs font-semibold">Rename Selected Meta Flow</Label>
                          <Input className="mt-1" value={renameMetaFlowName} onChange={(e) => setRenameMetaFlowName(e.target.value)} placeholder="Flow name" />
                        </div>
                        <Button variant="outline" onClick={updateMetaFlowName} disabled={metaFlowActionBusy || !selectedMetaFlowId} className="text-xs">
                          Update
                        </Button>
                        <Button variant="outline" onClick={publishMetaFlow} disabled={metaFlowActionBusy || !selectedMetaFlowId} className="text-xs">
                          Publish
                        </Button>
                        <Button variant="destructive" onClick={deleteMetaFlow} disabled={metaFlowActionBusy || !selectedMetaFlowId} className="col-span-2 text-xs">
                          Delete
                        </Button>
                      </div>
                      <Button className="w-full text-white" style={{ background: T.orange }} onClick={importFromMeta} disabled={metaFlowImportBusy || !selectedMetaFlowId}>
                        {metaFlowImportBusy ? "Importing..." : "Import into current flow"}
                      </Button>
                    </>
                  )}
                </div>
              </DialogContent>
            </Dialog>
            <Dialog open={showSendFlowDialog} onOpenChange={setShowSendFlowDialog}>
              <DialogTrigger asChild>
                <Button variant="outline" size="sm" className="h-7 text-xs gap-1">
                  <Send size={12} />Send Flow
                </Button>
              </DialogTrigger>
              <DialogContent className="sm:max-w-md">
                <DialogHeader>
                  <DialogTitle>Send Flow To User</DialogTitle>
                  <DialogDescription>Send WhatsApp interactive flow from this builder.</DialogDescription>
                </DialogHeader>
                <div className="space-y-3 pt-2">
                  <div>
                    <Label className="text-xs font-semibold">Recipient</Label>
                    <Input className="mt-1" value={sendFlowForm.recipient} onChange={(e) => setSendFlowForm((p) => ({ ...p, recipient: e.target.value }))} placeholder="9198XXXXXXXX" />
                  </div>
                  <div>
                    <Label className="text-xs font-semibold">Message</Label>
                    <Input className="mt-1" value={sendFlowForm.body} onChange={(e) => setSendFlowForm((p) => ({ ...p, body: e.target.value }))} />
                  </div>
                  <div className="grid grid-cols-2 gap-2">
                    <div>
                      <Label className="text-xs font-semibold">Flow Ref</Label>
                      <Input className="mt-1" value={sendFlowForm.flowRef} onChange={(e) => setSendFlowForm((p) => ({ ...p, flowRef: e.target.value }))} placeholder="meta_flow_id" />
                    </div>
                    <div>
                      <Label className="text-xs font-semibold">CTA</Label>
                      <Input className="mt-1" value={sendFlowForm.cta} onChange={(e) => setSendFlowForm((p) => ({ ...p, cta: e.target.value }))} />
                    </div>
                  </div>
                  <div>
                    <Label className="text-xs font-semibold">First Screen (optional)</Label>
                    <Input className="mt-1" value={sendFlowForm.screen} onChange={(e) => setSendFlowForm((p) => ({ ...p, screen: e.target.value }))} placeholder="welcome" />
                  </div>
                  <div>
                    <Label className="text-xs font-semibold">Data JSON</Label>
                    <Textarea rows={3} className="mt-1 font-mono text-xs" value={sendFlowForm.dataJson} onChange={(e) => setSendFlowForm((p) => ({ ...p, dataJson: e.target.value }))} />
                  </div>
                  <Button className="w-full text-white" style={{ background: T.orange }} onClick={sendFlowMessage} disabled={sendFlowBusy}>
                    {sendFlowBusy ? "Sending..." : "Send Flow"}
                  </Button>
                </div>
              </DialogContent>
            </Dialog>
            <Dialog open={showExchangeDialog} onOpenChange={setShowExchangeDialog}>
              <DialogTrigger asChild>
                <Button variant="outline" size="sm" className="h-7 text-xs gap-1">
                  <Webhook size={12} />Dynamic Data
                </Button>
              </DialogTrigger>
              <DialogContent className="sm:max-w-md">
                <DialogHeader>
                  <DialogTitle>Dynamic Data Exchange</DialogTitle>
                  <DialogDescription>Test data source action configured in flow JSON.</DialogDescription>
                </DialogHeader>
                <div className="space-y-3 pt-2">
                  <div>
                    <Label className="text-xs font-semibold">Action</Label>
                    <Input className="mt-1" value={exchangeForm.action} onChange={(e) => setExchangeForm((p) => ({ ...p, action: e.target.value }))} placeholder="customer_lookup" />
                  </div>
                  <div>
                    <Label className="text-xs font-semibold">Screen Id (optional)</Label>
                    <Input className="mt-1" value={exchangeForm.screenId} onChange={(e) => setExchangeForm((p) => ({ ...p, screenId: e.target.value }))} placeholder="welcome" />
                  </div>
                  <div>
                    <Label className="text-xs font-semibold">Payload JSON</Label>
                    <Textarea rows={4} className="mt-1 font-mono text-xs" value={exchangeForm.payloadJson} onChange={(e) => setExchangeForm((p) => ({ ...p, payloadJson: e.target.value }))} />
                  </div>
                  <Button className="w-full text-white" style={{ background: T.orange }} onClick={runDataExchange} disabled={exchangeBusy}>
                    {exchangeBusy ? "Running..." : "Run Exchange"}
                  </Button>
                </div>
              </DialogContent>
            </Dialog>
            <Dialog open={showEditFlow} onOpenChange={setShowEditFlow}>
              <DialogTrigger asChild>
                <Button variant="outline" size="sm" className="h-7 text-xs gap-1">
                  <Settings2 size={12} />Edit
                </Button>
              </DialogTrigger>
              <DialogContent className="sm:max-w-md">
                <DialogHeader>
                  <DialogTitle>Edit Flow</DialogTitle>
                  <DialogDescription>Update bot name, description and status.</DialogDescription>
                </DialogHeader>
                <div className="space-y-3 pt-2">
                  <div>
                    <Label className="text-xs font-semibold">Bot Name</Label>
                    <Input className="mt-1" value={editFlowForm.name} onChange={(e) => setEditFlowForm((p) => ({ ...p, name: e.target.value }))} />
                  </div>
                  <div>
                    <Label className="text-xs font-semibold">Description</Label>
                    <Textarea rows={2} className="mt-1" value={editFlowForm.description} onChange={(e) => setEditFlowForm((p) => ({ ...p, description: e.target.value }))} />
                  </div>
                  <label className="flex items-center gap-2 text-sm text-slate-700">
                    <input
                      type="checkbox"
                      checked={editFlowForm.isActive}
                      onChange={(e) => setEditFlowForm((p) => ({ ...p, isActive: e.target.checked }))}
                    />
                    Active
                  </label>
                  <Button className="w-full text-white" style={{ background: T.orange }} onClick={updateSelectedFlow}>
                    Update Flow
                  </Button>
                </div>
              </DialogContent>
            </Dialog>
            <Button size="sm" className="h-7 text-xs gap-1 text-white" style={{ background: T.orange }}
              onClick={() => publish()} disabled={!canPublishBot && selectedFlow?.lifecycleStatus !== "published" || validation.errors.length > 0}>
              <UploadCloud size={12} />Publish
            </Button>
            <Button size="sm" variant="ghost" className="h-7 text-xs gap-1 text-red-500 hover:bg-red-50" onClick={() => deleteFlow(selectedFlowId)}>
              <Trash2 size={12} />Delete
            </Button>
          </div>
        </div>

        {/* Canvas area */}
        <div className="flex-1 flex min-h-0">
          <div
            ref={canvasRef}
            className="flex-1 relative overflow-hidden select-none"
            style={{
              background: T.canvas,
              backgroundImage: `radial-gradient(${T.dots} 1px, transparent 1px)`,
              backgroundSize: `${18 * zoom}px ${18 * zoom}px`,
              backgroundPosition: `${pan.x % (18 * zoom)}px ${pan.y % (18 * zoom)}px`,
              cursor: draggingNodeId ? "grabbing" : "default",
            }}
            data-canvas-bg
            onMouseDown={onCanvasMouseDown}
            onMouseMove={onCanvasMouseMove}
            onMouseUp={onCanvasMouseUp}
          >
            {/* Empty state */}
            {nodes.length === 0 && (
              <div className="absolute inset-0 flex items-center justify-center pointer-events-none">
                <div className="text-center space-y-2 opacity-40">
                  <Plus size={48} className="mx-auto text-slate-400" strokeWidth={1} />
                  <div className="text-slate-500 font-medium">Click a node type in the palette to add it</div>
                  <div className="text-sm text-slate-400">Or drag nodes to arrange your flow</div>
                </div>
              </div>
            )}

            {/* Transform group */}
            <div style={{ transform: `translate(${pan.x}px, ${pan.y}px) scale(${zoom})`, transformOrigin: "0 0", position: "absolute", width: "100%", height: "100%" }}>
              {/* SVG Edges */}
              <svg style={{ position: "absolute", inset: 0, width: "100%", height: "100%", overflow: "visible", pointerEvents: "none" }}>
                {edges.map((e, i) => (
                  <EdgePath key={i} edge={e} nodes={nodes} pan={pan} />
                ))}
                {/* Drag-connect preview */}
                {dragConnect && (() => {
                  const fromNode = nodes.find((n) => n.id === dragConnect.from);
                  if (!fromNode) return null;
                  const sx = fromNode.x + NODE_W;
                  const sy = fromNode.y + NODE_H_BASE / 2;
                  const tx = (dragConnect.x2 - pan.x) / zoom;
                  const ty = (dragConnect.y2 - pan.y) / zoom;
                  const dx = Math.abs(tx - sx);
                  const pathD = `M ${sx} ${sy} C ${sx + Math.max(40, dx * 0.5)} ${sy}, ${tx - Math.max(40, dx * 0.5)} ${ty}, ${tx} ${ty}`;
                  return <path d={pathD} fill="none" stroke={T.warning} strokeWidth="2" strokeDasharray="6 3" />;
                })()}
              </svg>

              {/* Nodes */}
              {nodes.map((n) => (
                <CanvasNode
                  key={n.id}
                  node={n}
                  isSelected={selectedNodeId === n.id}
                  onSelect={setSelectedNodeId}
                  onStartConnect={onStartConnect}
                  onConnectTo={onConnectToNode}
                  onDelete={removeNode}
                  isDragging={draggingNodeId === n.id}
                  onDragStart={onNodeDragStart}
                  zoom={zoom}
                />
              ))}
            </div>

            {/* Node count badge */}
            <div className="absolute bottom-3 left-3 text-[10px] text-slate-400 bg-white border border-slate-200 rounded-full px-2 py-1">
              {nodes.length} nodes · {edges.length} connections
            </div>
          </div>

          {/* ── Version history sidebar ── */}
          {showVersions && (
            <div className="hidden 2xl:flex w-56 xl:w-60 flex-shrink-0 border-l border-slate-200 bg-white flex-col min-w-0">
              <div className="px-4 py-3 border-b border-slate-100 flex items-center justify-between">
                <div className="text-xs font-bold text-slate-600 uppercase tracking-wide flex items-center gap-1.5"><Clock size={12} />Version History</div>
                <button className="text-slate-400 hover:text-slate-600" onClick={() => setShowVersions(false)}><X size={14} /></button>
              </div>
              <ScrollArea className="flex-1">
                <div className="p-3 space-y-2">
                  {!versions.length && <div className="text-xs text-slate-400 text-center py-4">No versions saved yet</div>}
                  {versions.map((v, i) => (
                    <div key={v.id} className={`rounded-xl border p-3 ${i === 0 ? "border-orange-200 bg-orange-50" : "border-slate-200"}`}>
                      <div className="flex items-center justify-between mb-1">
                        <span className="text-[10px] font-bold uppercase" style={{ color: i === 0 ? T.orange : "#94a3b8" }}>
                          {i === 0 ? "Latest" : `v${versions.length - i}`}
                        </span>
                        <span className="text-[10px] text-slate-400">{v.createdAtUtc ? new Date(v.createdAtUtc).toLocaleDateString() : "—"}</span>
                      </div>
                      <div className="text-xs text-slate-600">{v.changeNote || "No note"}</div>
                      <div className="text-[10px] text-slate-400 mt-1">{v.createdAtUtc ? new Date(v.createdAtUtc).toLocaleTimeString() : ""}</div>
                      {i > 0 && (
                        <button className="mt-2 text-[10px] text-orange-600 hover:underline font-medium" onClick={() => {
                          try {
                            const def = JSON.parse(v.definitionJson);
                            if (def.nodes) { resetNodes(def.nodes); toast.success("Rolled back to this version"); setShowVersions(false); }
                          } catch { toast.error("Failed to parse version"); }
                        }}>
                          ↩ Restore this version
                        </button>
                      )}
                    </div>
                  ))}
                </div>
              </ScrollArea>
            </div>
          )}

          {/* ── Right panel ── */}
          {showPreview && (
            <div className="hidden 2xl:flex w-[280px] 2xl:w-72 flex-shrink-0 border-l border-slate-200 bg-white flex-col min-w-0">
              <div className="border-b border-slate-100 px-4 py-3 flex items-center justify-between">
                <div className="text-xs font-bold text-slate-600 uppercase tracking-wide flex items-center gap-1.5">
                  <Settings2 size={12} />Configure
                </div>
                <div className="flex items-center gap-1">
                  <Eye size={12} className="text-slate-400" />
                  <span className="text-[10px] text-slate-400">Preview</span>
                </div>
              </div>

              <div className="flex-1 overflow-hidden flex flex-col">
                {/* Config panel */}
                <div className="flex-1 overflow-hidden" style={{ maxHeight: showPreview && selectedNode ? "55%" : "100%" }}>
                  <NodePanel
                    node={selectedNode}
                    nodes={nodes}
                    onUpdate={updateNode}
                    onDelete={removeNode}
                    onDuplicate={duplicateNode}
                    onDisconnect={disconnectNode}
                  />
                </div>

                {/* WhatsApp preview */}
                {selectedNode && (
                  <div className="border-t border-slate-100 flex-shrink-0" style={{ maxHeight: "45%" }}>
                    <div className="px-4 py-2 flex items-center justify-between">
                      <span className="text-[10px] font-bold uppercase tracking-wide text-slate-400 flex items-center gap-1">
                        <MessageCircle size={10} style={{ color: T.whatsapp }} />WhatsApp Preview
                      </span>
                      {outside24h && ["text","bot_reply"].includes(selectedNode.type) && (
                        <span className="text-[10px] flex items-center gap-1" style={{ color: T.warning }}>
                          <AlertTriangle size={10} />Use template
                        </span>
                      )}
                    </div>
                    <div className="px-3 pb-3 overflow-auto" style={{ maxHeight: "calc(45vh - 48px)" }}>
                      <WhatsAppPreview node={selectedNode} />
                    </div>
                  </div>
                )}
              </div>
            </div>
          )}
        </div>
      </div>

      {showPaletteDrawer && (
        <div className="xl:hidden absolute inset-0 z-30 flex">
          <div className="w-[320px] max-w-[88vw] border-r border-slate-200 bg-white shadow-2xl flex flex-col min-w-0">
            <div className="px-4 py-3 border-b border-slate-100 flex items-center justify-between">
              <div className="text-xs font-bold text-slate-600 uppercase tracking-wide flex items-center gap-1.5"><Layers size={12} />Nodes</div>
              <button className="text-slate-400 hover:text-slate-600" onClick={() => setShowPaletteDrawer(false)}><X size={14} /></button>
            </div>
            <div className="p-3 border-b border-slate-100">
              <div className="relative">
                <Search size={13} className="absolute left-2.5 top-2.5 text-slate-400" />
                <Input value={searchQuery} onChange={(e) => setSearchQuery(e.target.value)} className="pl-8 h-8 text-xs" placeholder="Search nodes..." />
              </div>
            </div>
            <div className="border-b border-slate-100 p-3 space-y-2">
              <Label className="text-[10px] font-bold uppercase tracking-wide text-slate-400 flex items-center gap-1"><Zap size={10} />Trigger Keywords</Label>
              <Input value={triggerKeywords} onChange={(e) => setTriggerKeywords(e.target.value)} className="h-7 text-xs" placeholder="hi,hello,support" />
              <Button size="sm" className="w-full h-7 text-xs text-white" style={{ background: T.orange }} onClick={saveTriggerKeywords}>Save Trigger</Button>
            </div>
            <ScrollArea className="flex-1">
              <div className="p-3 space-y-3">
                {filteredSections.map((section) => {
                  const SIcon = section.icon;
                  const isOpen = activeSection === section.id || searchQuery;
                  return (
                    <div key={`drawer-${section.id}`}>
                      <button
                        className="w-full flex items-center justify-between py-1.5 px-2 rounded-lg text-xs font-bold uppercase tracking-wide hover:bg-slate-50 transition-colors"
                        style={{ color: section.color }}
                        onClick={() => setActiveSection(isOpen && !searchQuery ? null : section.id)}
                      >
                        <div className="flex items-center gap-1.5"><SIcon size={11} />{section.label}</div>
                        {!searchQuery && (isOpen ? <ChevronDown size={11} /> : <ChevronRight size={11} />)}
                      </button>
                      {isOpen && (
                        <div className="mt-1.5 space-y-1">
                          {section.items.map((type) => {
                            const meta = NODE_META[type];
                            const MIcon = meta?.icon || MessageCircle;
                            return (
                              <button
                                key={`drawer-item-${type}`}
                                className="w-full flex items-center gap-2.5 rounded-lg px-2.5 py-2 text-left hover:shadow-sm transition-all"
                                style={{ background: meta?.bg, border: `1px solid ${meta?.border}` }}
                                onClick={() => {
                                  addNode(type);
                                  setShowPaletteDrawer(false);
                                }}
                              >
                                <div className="w-6 h-6 rounded-md flex items-center justify-center flex-shrink-0" style={{ background: meta?.color }}>
                                  <MIcon size={11} color="white" />
                                </div>
                                <div className="min-w-0">
                                  <div className="text-[11px] font-semibold text-slate-800 leading-tight">{meta?.label}</div>
                                  <div className="text-[10px] text-slate-400 leading-tight truncate">{meta?.hint}</div>
                                </div>
                              </button>
                            );
                          })}
                        </div>
                      )}
                    </div>
                  );
                })}
              </div>
            </ScrollArea>
          </div>
          <button className="flex-1 bg-slate-900/35" onClick={() => setShowPaletteDrawer(false)} aria-label="Close nodes drawer" />
        </div>
      )}

      {showVersions && (
        <div className="2xl:hidden absolute inset-0 z-30 flex justify-end">
          <button className="flex-1 bg-slate-900/35" onClick={() => setShowVersions(false)} aria-label="Close version history" />
          <div className="w-[320px] max-w-[88vw] border-l border-slate-200 bg-white shadow-2xl flex flex-col min-w-0">
            <div className="px-4 py-3 border-b border-slate-100 flex items-center justify-between">
              <div className="text-xs font-bold text-slate-600 uppercase tracking-wide flex items-center gap-1.5"><Clock size={12} />Version History</div>
              <button className="text-slate-400 hover:text-slate-600" onClick={() => setShowVersions(false)}><X size={14} /></button>
            </div>
            <ScrollArea className="flex-1">
              <div className="p-3 space-y-2">
                {!versions.length && <div className="text-xs text-slate-400 text-center py-4">No versions saved yet</div>}
                {versions.map((v, i) => (
                  <div key={`drawer-version-${v.id}`} className={`rounded-xl border p-3 ${i === 0 ? "border-orange-200 bg-orange-50" : "border-slate-200"}`}>
                    <div className="flex items-center justify-between mb-1">
                      <span className="text-[10px] font-bold uppercase" style={{ color: i === 0 ? T.orange : "#94a3b8" }}>
                        {i === 0 ? "Latest" : `v${versions.length - i}`}
                      </span>
                      <span className="text-[10px] text-slate-400">{v.createdAtUtc ? new Date(v.createdAtUtc).toLocaleDateString() : "—"}</span>
                    </div>
                    <div className="text-xs text-slate-600">{v.changeNote || "No note"}</div>
                    <div className="text-[10px] text-slate-400 mt-1">{v.createdAtUtc ? new Date(v.createdAtUtc).toLocaleTimeString() : ""}</div>
                    {i > 0 && (
                      <button className="mt-2 text-[10px] text-orange-600 hover:underline font-medium" onClick={() => {
                        try {
                          const def = JSON.parse(v.definitionJson);
                          if (def.nodes) { resetNodes(def.nodes); toast.success("Rolled back to this version"); setShowVersions(false); }
                        } catch { toast.error("Failed to parse version"); }
                      }}>
                        ↩ Restore this version
                      </button>
                    )}
                  </div>
                ))}
              </div>
            </ScrollArea>
          </div>
        </div>
      )}

      {showPreview && showInspectorDrawer && (
        <div className="2xl:hidden absolute inset-0 z-30 flex justify-end">
          <button className="flex-1 bg-slate-900/35" onClick={() => setShowInspectorDrawer(false)} aria-label="Close inspector drawer" />
          <div className="w-[340px] max-w-[92vw] border-l border-slate-200 bg-white shadow-2xl flex flex-col min-w-0">
            <div className="border-b border-slate-100 px-4 py-3 flex items-center justify-between">
              <div className="text-xs font-bold text-slate-600 uppercase tracking-wide flex items-center gap-1.5">
                <Settings2 size={12} />Configure
              </div>
              <Button type="button" variant="ghost" size="icon" className="h-8 w-8" onClick={() => setShowInspectorDrawer(false)}>
                <X size={14} />
              </Button>
            </div>
            <div className="flex-1 overflow-hidden flex flex-col">
              <div className="flex-1 overflow-hidden" style={{ maxHeight: showPreview && selectedNode ? "55%" : "100%" }}>
                <NodePanel
                  node={selectedNode}
                  nodes={nodes}
                  onUpdate={updateNode}
                  onDelete={removeNode}
                  onDuplicate={duplicateNode}
                  onDisconnect={disconnectNode}
                />
              </div>
              {selectedNode && (
                <div className="border-t border-slate-100 flex-shrink-0" style={{ maxHeight: "45%" }}>
                  <div className="px-4 py-2 flex items-center justify-between">
                    <span className="text-[10px] font-bold uppercase tracking-wide text-slate-400 flex items-center gap-1">
                      <MessageCircle size={10} style={{ color: T.whatsapp }} />WhatsApp Preview
                    </span>
                    {outside24h && ["text","bot_reply"].includes(selectedNode.type) && (
                      <span className="text-[10px] flex items-center gap-1" style={{ color: T.warning }}>
                        <AlertTriangle size={10} />Use template
                      </span>
                    )}
                  </div>
                  <div className="px-3 pb-3 overflow-auto" style={{ maxHeight: "calc(45vh - 48px)" }}>
                    <WhatsAppPreview node={selectedNode} />
                  </div>
                </div>
              )}
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

/* ═══════════════════════════════════════════════════════════════════════════════
   Q&A PAGE
═══════════════════════════════════════════════════════════════════════════════ */
function QaPage({ faqItems, faqForm, setFaqForm, saveFaq, deleteFaq, editingFaqId, startEditFaq, cancelEditFaq }) {
  const [search, setSearch] = useState("");
  const filtered = faqItems.filter((i) =>
    !search || i.question.toLowerCase().includes(search.toLowerCase()) || i.answer.toLowerCase().includes(search.toLowerCase())
  );

  return (
    <div className="p-6 h-full overflow-auto">
      <div className="max-w-5xl mx-auto space-y-6">
        <div className="flex items-center justify-between">
          <div>
            <h1 className="text-xl font-bold text-slate-800">Q&A Knowledge Base</h1>
            <p className="text-sm text-slate-500 mt-0.5">The bot searches this before escalating to a human agent</p>
          </div>
          <Badge className="bg-orange-100 text-orange-700 border-orange-200">{faqItems.length} entries</Badge>
        </div>

        <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
          {/* Add form */}
          <div className="bg-white rounded-2xl border border-slate-200 p-5 space-y-4">
            <h2 className="font-semibold text-slate-800 flex items-center gap-2">
              {editingFaqId ? <><Settings2 size={15} />Edit Entry</> : <><Plus size={15} />Add New Entry</>}
            </h2>
            <div className="space-y-3">
              <div>
                <Label className="text-xs font-semibold">Question *</Label>
                <Input className="mt-1" value={faqForm.question} onChange={(e) => setFaqForm((p) => ({ ...p, question: e.target.value }))} placeholder="What are your business hours?" />
              </div>
              <div>
                <Label className="text-xs font-semibold">Answer *</Label>
                <Textarea className="mt-1" rows={4} value={faqForm.answer} onChange={(e) => setFaqForm((p) => ({ ...p, answer: e.target.value }))} placeholder="We're open Monday-Friday, 9am-6pm IST." />
              </div>
              <div>
                <Label className="text-xs font-semibold">Category</Label>
                <Input className="mt-1" value={faqForm.category} onChange={(e) => setFaqForm((p) => ({ ...p, category: e.target.value }))} placeholder="General, Billing, Technical..." />
              </div>
              <div className="flex gap-2">
                <Button className="flex-1 text-white" style={{ background: T.orange }} onClick={saveFaq} disabled={!faqForm.question.trim() || !faqForm.answer.trim()}>
                  {editingFaqId ? "Update Entry" : "Add Entry"}
                </Button>
                {editingFaqId && (
                  <Button variant="outline" onClick={cancelEditFaq}>Cancel</Button>
                )}
              </div>
            </div>
          </div>

          {/* List */}
          <div className="bg-white rounded-2xl border border-slate-200 overflow-hidden flex flex-col">
            <div className="p-4 border-b border-slate-100">
              <div className="relative">
                <Search size={13} className="absolute left-2.5 top-2.5 text-slate-400" />
                <Input className="pl-8 h-8 text-xs" placeholder="Search Q&A..." value={search} onChange={(e) => setSearch(e.target.value)} />
              </div>
            </div>
            <ScrollArea className="flex-1 p-4">
              <div className="space-y-3">
                {filtered.length === 0 && (
                  <div className="text-center py-8 text-slate-400 text-sm">
                    {search ? "No results found" : "No Q&A entries yet"}
                  </div>
                )}
                {filtered.map((item) => (
                  <div key={item.id} className={`rounded-xl border p-3 hover:border-orange-200 transition-colors group ${editingFaqId === item.id ? "border-orange-300 bg-orange-50" : "border-slate-200"}`}>
                    <div className="flex items-start justify-between gap-2">
                      <div className="flex-1 min-w-0">
                        <div className="font-medium text-sm text-slate-800">{item.question}</div>
                        <div className="text-xs text-slate-500 mt-1 line-clamp-2">{item.answer}</div>
                        {item.category && (
                          <span className="inline-block mt-1.5 text-[10px] bg-orange-50 text-orange-600 border border-orange-200 rounded-full px-2 py-0.5">{item.category}</span>
                        )}
                      </div>
                      <div className="flex gap-1 flex-shrink-0 opacity-0 group-hover:opacity-100 transition-opacity">
                        <button className="text-slate-400 hover:text-orange-500 p-1 rounded" onClick={() => startEditFaq(item)} title="Edit">
                          <Settings2 size={13} />
                        </button>
                        <button className="text-slate-400 hover:text-red-500 p-1 rounded" onClick={() => deleteFaq(item.id)} title="Delete">
                          <Trash2 size={13} />
                        </button>
                      </div>
                    </div>
                  </div>
                ))}
              </div>
            </ScrollArea>
          </div>
        </div>
      </div>
    </div>
  );
}
