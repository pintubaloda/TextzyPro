import { useEffect, useMemo, useState } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Card, CardContent, CardHeader } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Textarea } from "@/components/ui/textarea";
import { Label } from "@/components/ui/label";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger, DropdownMenuSeparator } from "@/components/ui/dropdown-menu";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle, DialogTrigger } from "@/components/ui/dialog";
import { AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent, AlertDialogDescription, AlertDialogFooter, AlertDialogHeader, AlertDialogTitle } from "@/components/ui/alert-dialog";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Search, Plus, Filter, MoreVertical, FileText, CheckCircle, XCircle, Clock, AlertCircle, Trash2, Send, Image as ImageIcon, Video, Paperclip, Upload } from "lucide-react";
import { apiDelete, apiGet, apiPost, apiPostForm } from "@/lib/api";
import { toast } from "sonner";

const initialDraft = {
  name: "",
  body: "",
  channel: "whatsapp",
  category: "MARKETING",
  language: "en",
  dltEntityId: "",
  dltTemplateId: "",
  smsSenderId: "",
  headerType: "none",
  headerText: "",
  headerMediaId: "",
  headerMediaName: "",
  footerText: "",
  buttonsJson: "",
};

function normalizeListPayload(payload) {
  if (Array.isArray(payload)) return payload;
  if (Array.isArray(payload?.items)) return payload.items;
  if (Array.isArray(payload?.items?.$values)) return payload.items.$values;
  if (Array.isArray(payload?.data)) return payload.data;
  if (Array.isArray(payload?.data?.$values)) return payload.data.$values;
  if (Array.isArray(payload?.templates)) return payload.templates;
  if (Array.isArray(payload?.templates?.$values)) return payload.templates.$values;
  if (Array.isArray(payload?.results)) return payload.results;
  if (Array.isArray(payload?.results?.$values)) return payload.results.$values;
  if (Array.isArray(payload?.$values)) return payload.$values;
  if (payload && typeof payload === "object") {
    const values = Object.values(payload);
    if (values.length > 0 && values.every((v) => v && typeof v === "object")) return values;
  }
  return [];
}

function channelValue(x) {
  const raw = x?.channel ?? x?.Channel;
  if (typeof raw === "number") return raw;
  const text = String(raw || "").trim().toLowerCase();
  if (text === "whatsapp") return 2;
  if (text === "sms") return 1;
  const num = Number(raw);
  return Number.isFinite(num) ? num : 0;
}

function nameValue(x) {
  return x?.name ?? x?.Name ?? "";
}

function bodyValue(x) {
  return x?.body ?? x?.Body ?? "";
}

function statusValue(x) {
  return x?.status ?? x?.Status ?? x?.lifecycleStatus ?? x?.LifecycleStatus ?? "";
}

function categoryValue(x) {
  return x?.category ?? x?.Category ?? "";
}

function languageValue(x) {
  return x?.language ?? x?.Language ?? "en";
}

function idValue(x) {
  return x?.id ?? x?.Id;
}

function inSelectedTab(template, tab) {
  const ch = channelValue(template);
  if (tab === "sms") return ch === 1;
  return ch !== 1;
}

const categoryGuide = {
  MARKETING: {
    title: "Marketing",
    color: "text-purple-700",
    hint: "Promotions, offers, product discovery, re-engagement.",
    must: "Use clear promotional copy. Prefer CTA/Quick replies.",
  },
  UTILITY: {
    title: "Utility",
    color: "text-blue-700",
    hint: "Transactional updates for opted-in users (order/account/service).",
    must: "Message should be service-oriented and contextual.",
  },
  AUTHENTICATION: {
    title: "Authentication",
    color: "text-orange-700",
    hint: "OTP/login verification and security auth messages.",
    must: "Should include auth code variable (typically {{1}}).",
  },
};

const nameRegex = /^[a-z0-9_]+$/;
const blockedShortenerHosts = ["bit.ly", "tinyurl.com", "t.co", "shorturl.at", "rb.gy", "goo.gl", "ow.ly", "is.gd", "cutt.ly", "buff.ly"];

function hasBlockedShortener(text) {
  const value = String(text || "");
  const matches = value.match(/https?:\/\/[^\s]+/gi) || [];
  return matches.some((url) => {
    try {
      const host = new URL(url).hostname.toLowerCase();
      return blockedShortenerHosts.includes(host) || blockedShortenerHosts.some((x) => host.endsWith(`.${x}`));
    } catch {
      return false;
    }
  });
}

function parseButtons(buttonsJson) {
  try {
    const parsed = JSON.parse(buttonsJson || "[]");
    return Array.isArray(parsed) ? parsed : [];
  } catch {
    return [];
  }
}

const TemplatePreviewCard = ({ template }) => {
  const buttons = parseButtons(template?.buttonsJson || template?.ButtonsJson || "[]");
  const body = template?.body || template?.Body || "";
  const headerType = (template?.headerType || template?.HeaderType || "none").toLowerCase();
  const headerText = template?.headerText || template?.HeaderText || "";
  const footerText = template?.footerText || template?.FooterText || "";
  const headerMediaName = template?.headerMediaName || template?.HeaderMediaName || "";

  return (
    <div className="rounded-xl border border-slate-200 bg-white p-4">
      <div className="mx-auto w-full max-w-sm rounded-2xl border bg-[#efeae2] p-3">
        <div className="rounded-2xl bg-white shadow-sm overflow-hidden">
          {(headerType === "text" && headerText) ? (
            <div className="px-3 pt-3 text-sm font-semibold text-slate-800">{headerText}</div>
          ) : null}

          {(headerType === "image" || headerType === "video" || headerType === "document") ? (
            <div className="mx-3 mt-3 rounded-lg border border-slate-200 bg-slate-50 p-4 text-xs text-slate-600 flex items-center gap-2">
              {headerType === "image" ? <ImageIcon className="w-4 h-4" /> : null}
              {headerType === "video" ? <Video className="w-4 h-4" /> : null}
              {headerType === "document" ? <Paperclip className="w-4 h-4" /> : null}
              <span>{headerMediaName || `${headerType} header attached`}</span>
            </div>
          ) : null}

          <div className="px-3 py-3 text-[15px] leading-6 text-slate-900 whitespace-pre-wrap">{body || "Template body preview"}</div>
          {footerText ? <div className="px-3 pb-2 text-xs text-slate-500">{footerText}</div> : null}

          {buttons.length > 0 ? (
            <div className="border-t border-slate-200">
              {buttons.map((b, i) => (
                <div key={i} className="px-3 py-2 text-sm text-blue-700 border-b last:border-b-0 border-slate-100">
                  {b?.text || "Button"}
                </div>
              ))}
            </div>
          ) : null}
        </div>
      </div>
    </div>
  );
};

const TemplatesPage = () => {
  const navigate = useNavigate();
  const [showCreateDialog, setShowCreateDialog] = useState(false);
  const [showPreviewDialog, setShowPreviewDialog] = useState(false);
  const [previewTemplate, setPreviewTemplate] = useState(null);
  const [searchParams] = useSearchParams();
  const tab = "whatsapp";

  const [templates, setTemplates] = useState([]);
  const [draft, setDraft] = useState(initialDraft);
  const [tableSearch, setTableSearch] = useState("");
  const [uploading, setUploading] = useState(false);
  const [deleteTarget, setDeleteTarget] = useState(null);
  const [deleteBusy, setDeleteBusy] = useState(false);
  const [wizardStep, setWizardStep] = useState(1);

  const isMediaHeader = ["image", "video", "document"].includes((draft.headerType || "none").toLowerCase());

  useEffect(() => {
    loadAll();
  }, []);

  useEffect(() => {
    if (searchParams.get("tab") === "sms") {
      navigate("/dashboard/sms-setup?panel=templates", { replace: true });
      return;
    }
    setDraft((p) => ({ ...p, channel: "whatsapp" }));
  }, [navigate, searchParams]);

  const loadAll = async () => {
    try {
      const tpl = await apiGet("/api/templates/project-list");
      const projectTemplates = normalizeListPayload(tpl?.items ?? tpl?.Items ?? tpl);
      setTemplates(projectTemplates);
    } catch (e) {
      setTemplates([]);
      toast.error(e?.message || "Template list load failed");
    }
  };

  const stats = [
    { title: "Total Templates", value: String(templates.filter((t) => inSelectedTab(t, tab)).length) },
    { title: "Approved", value: String(templates.filter((t) => inSelectedTab(t, tab) && String(statusValue(t)).toLowerCase() === "approved").length) },
    { title: "Pending", value: String(templates.filter((t) => inSelectedTab(t, tab) && String(statusValue(t)).toLowerCase() === "pending").length) },
    { title: "Rejected", value: String(templates.filter((t) => inSelectedTab(t, tab) && String(statusValue(t)).toLowerCase() === "rejected").length) },
  ];

  const templateVars = useMemo(() => {
    const matches = [...(draft.body || "").matchAll(/\{\{(\d+)\}\}/g)].map((m) => Number(m[1]));
    return Array.from(new Set(matches)).sort((a, b) => a - b);
  }, [draft.body]);

  const filteredTemplates = useMemo(() => {
    const q = tableSearch.trim().toLowerCase();
    return templates.filter((template) => {
      const inTab = inSelectedTab(template, tab);
      if (!inTab) return false;
      if (!q) return true;
      return String(nameValue(template)).toLowerCase().includes(q) || String(bodyValue(template)).toLowerCase().includes(q);
    });
  }, [templates, tab, tableSearch]);

  const validateDraft = () => {
    if (!draft.name.trim()) return "Template name is required.";
    if (!draft.body.trim()) return "Message body is required.";
    if (draft.channel === "whatsapp") {
      const normalizedName = draft.name.trim();
      if (!nameRegex.test(normalizedName)) return "Template name: lowercase letters, numbers, underscore only (no spaces).";
      if (draft.body.length > 1024) return "WhatsApp template body max length is 1024.";
      for (let i = 0; i < templateVars.length; i += 1) {
        if (templateVars[i] !== i + 1) return "Variables must be sequential: {{1}}, {{2}}, {{3}}...";
      }
      if (hasBlockedShortener(`${draft.body} ${draft.footerText} ${draft.buttonsJson}`)) {
        return "URL shortener domains are not allowed in WhatsApp templates.";
      }
      if (draft.category === "UTILITY" || draft.category === "AUTHENTICATION") {
        const content = `${draft.body} ${draft.footerText}`.toLowerCase();
        if (["buy now", "limited time", "flash sale", "promo code", "discount"].some((x) => content.includes(x))) {
          return `${draft.category} templates cannot include promotional marketing language.`;
        }
      }
      if (String(draft.category).toUpperCase() === "AUTHENTICATION" && !draft.body.includes("{{1}}")) {
        return "Authentication category must include auth variable ({{1}}).";
      }
      if (isMediaHeader && !draft.headerMediaId.trim()) {
        return "Please upload header media for selected media header type.";
      }
      if (draft.headerType === "text" && draft.headerText.trim().length > 60) return "Header text max length is 60.";
      if (draft.footerText.trim().length > 60) return "Footer text max length is 60.";
    }
    return "";
  };

  const createTemplate = async () => {
    const error = validateDraft();
    if (error) {
      toast.error(error);
      return;
    }
    try {
      await apiPost("/api/templates", {
        name: draft.name.trim(),
        body: draft.body,
        channel: draft.channel === "sms" ? 1 : 2,
        category: (draft.category || "UTILITY").toUpperCase(),
        language: draft.language,
        dltEntityId: draft.dltEntityId.trim(),
        dltTemplateId: draft.dltTemplateId.trim(),
        smsSenderId: draft.smsSenderId.trim().toUpperCase(),
        headerType: draft.headerType,
        headerText: draft.headerText,
        headerMediaId: draft.headerMediaId,
        headerMediaName: draft.headerMediaName,
        footerText: draft.footerText,
        buttonsJson: draft.buttonsJson,
      });
      toast.success("Template created.");
      setDraft(initialDraft);
      setShowCreateDialog(false);
      await loadAll();
    } catch (e) {
      toast.error(e?.message || "Template create failed.");
    }
  };

  const uploadHeaderMedia = async (file) => {
    if (!file) return;
    if (!isMediaHeader) {
      toast.error("Select media header type first.");
      return;
    }
    try {
      setUploading(true);
      const form = new FormData();
      form.append("file", file);
      form.append("mediaType", draft.headerType);
      const out = await apiPostForm("/api/messages/upload-whatsapp-asset", form);
      setDraft((p) => ({ ...p, headerMediaId: out?.mediaId || "", headerMediaName: out?.fileName || file.name }));
      toast.success("Header media uploaded.");
    } catch (e) {
      toast.error(e?.message || "Header media upload failed.");
    } finally {
      setUploading(false);
    }
  };

  const applyWizardPreset = (preset) => {
    if (preset === "utility") {
      setDraft((p) => ({
        ...p,
        channel: "whatsapp",
        category: "UTILITY",
        name: "order_update_v1",
        body: "Hi {{1}}, your order {{2}} is confirmed and will reach by {{3}}.",
        footerText: "Reply STOP to opt-out",
        headerType: "none",
        headerText: "",
        buttonsJson: '[{"type":"quick_reply","text":"Track Order"},{"type":"quick_reply","text":"Support"}]',
      }));
      setWizardStep(2);
      return;
    }

    if (preset === "otp") {
      setDraft((p) => ({
        ...p,
        channel: "whatsapp",
        category: "AUTHENTICATION",
        name: "login_otp_v1",
        body: "Your verification code is {{1}}. It is valid for 10 minutes.",
        footerText: "Do not share this code with anyone.",
        headerType: "text",
        headerText: "Verification code",
        buttonsJson: "[]",
      }));
      setWizardStep(3);
    }
  };

  const removeTemplate = async () => {
    const targetId = idValue(deleteTarget);
    if (!targetId || deleteBusy) return;
    try {
      setDeleteBusy(true);
      await apiDelete(`/api/templates/${targetId}`);
      setTemplates((prev) => prev.filter((x) => idValue(x) !== targetId));
      toast.success("Template deleted");
      setDeleteTarget(null);
    } catch (e) {
      toast.error(e?.message || "Delete failed");
    } finally {
      setDeleteBusy(false);
    }
  };

  const syncWhatsAppTemplates = async () => {
    try {
      await apiPost("/api/template-lifecycle/sync?rebuild=true", {});
      toast.success("WhatsApp templates rebuilt and synced from Meta.");
      await loadAll();
    } catch (e) {
      toast.error(e?.message || "Sync failed.");
    }
  };

  const submitTemplateToMeta = async (id) => {
    try {
      await apiPost(`/api/template-lifecycle/${id}/submit`, {});
      toast.success("Template submitted to Meta.");
      await loadAll();
    } catch (e) {
      toast.error(e?.message || "Submit failed.");
    }
  };

  const getStatusBadge = (statusRaw) => {
    const status = String(statusRaw || "").toLowerCase();
    if (status === "approved") return <Badge className="bg-green-100 text-green-700 hover:bg-green-100 gap-1"><CheckCircle className="w-3 h-3" />Approved</Badge>;
    if (status === "pending") return <Badge className="bg-yellow-100 text-yellow-700 hover:bg-yellow-100 gap-1"><Clock className="w-3 h-3" />Pending</Badge>;
    if (status === "rejected") return <Badge className="bg-red-100 text-red-700 hover:bg-red-100 gap-1"><XCircle className="w-3 h-3" />Rejected</Badge>;
    return <Badge variant="outline">{statusRaw || "draft"}</Badge>;
  };

  const getCategoryBadge = (categoryRaw) => {
    const category = String(categoryRaw || "").toLowerCase();
    if (category === "marketing") return <Badge variant="outline" className="bg-purple-50 text-purple-700 border-purple-200">Marketing</Badge>;
    if (category === "utility") return <Badge variant="outline" className="bg-blue-50 text-blue-700 border-blue-200">Utility</Badge>;
    if (category === "authentication") return <Badge variant="outline" className="bg-orange-50 text-orange-700 border-orange-200">Authentication</Badge>;
    return <Badge variant="outline">{categoryRaw}</Badge>;
  };

  const guide = categoryGuide[(draft.category || "UTILITY").toUpperCase()] || categoryGuide.UTILITY;

  return (
    <>
      <div className="space-y-6" data-testid="templates-page">
        <div className="flex flex-col md:flex-row md:items-center md:justify-between gap-4">
          <div>
            <h1 className="text-2xl font-heading font-bold text-slate-900">Templates</h1>
            <p className="text-slate-600">WhatsApp template management synced with Meta.</p>
          </div>
          <div className="flex items-center gap-2 flex-wrap">
            <Badge className="bg-orange-100 text-orange-700 border-orange-200 hover:bg-orange-100">WhatsApp</Badge>
            <Button variant="outline" onClick={syncWhatsAppTemplates}>Sync Meta</Button>
            <Button variant="outline" onClick={() => navigate("/dashboard/sms-setup?panel=templates")}>Open SMS Registry</Button>
            <Dialog open={showCreateDialog} onOpenChange={setShowCreateDialog}>
              <DialogTrigger asChild>
                <Button className="bg-orange-500 hover:bg-orange-600 text-white gap-2" data-testid="create-template-btn">
                  <Plus className="w-4 h-4" />
                  Create Template
                </Button>
              </DialogTrigger>
                <DialogContent className="max-w-6xl max-h-[92vh] p-0 overflow-hidden">
                <DialogHeader className="px-6 pt-6 pb-2 border-b bg-white">
                  <DialogTitle>Create Template</DialogTitle>
                  <DialogDescription>Build and manage approved WhatsApp templates.</DialogDescription>
                </DialogHeader>

                <div className="grid grid-cols-1 lg:grid-cols-5 gap-0">
                  <div className="lg:col-span-3 px-6 py-4 overflow-y-auto max-h-[calc(92vh-170px)] space-y-4 border-r">
                    {draft.channel === "whatsapp" && (
                      <div className="rounded-lg border border-blue-200 bg-blue-50 p-3 space-y-3">
                        <div className="font-semibold text-slate-900">Template Setup Wizard</div>
                        <div className="text-xs text-slate-700">Step {wizardStep}/3: Create utility and authentication starter templates after onboarding.</div>
                        <div className="flex flex-wrap gap-2">
                          <Button type="button" size="sm" variant="outline" onClick={() => applyWizardPreset("utility")}>Use Utility Starter</Button>
                          <Button type="button" size="sm" variant="outline" onClick={() => applyWizardPreset("otp")}>Use OTP Starter</Button>
                          <Button type="button" size="sm" variant="ghost" onClick={() => setWizardStep(1)}>Reset Wizard</Button>
                        </div>
                      </div>
                    )}

                    <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                      <div className="space-y-2">
                        <Label>Template Name</Label>
                        <Input placeholder="e.g., order_confirmation_v1" value={draft.name} onChange={(e) => setDraft((p) => ({ ...p, name: e.target.value }))} />
                      </div>
                      <div className="space-y-2">
                        <Label>Language</Label>
                        <Select value={draft.language} onValueChange={(v) => setDraft((p) => ({ ...p, language: v }))}>
                          <SelectTrigger><SelectValue placeholder="Language" /></SelectTrigger>
                          <SelectContent>
                            <SelectItem value="en">English</SelectItem>
                            <SelectItem value="hi">Hindi</SelectItem>
                            <SelectItem value="ta">Tamil</SelectItem>
                            <SelectItem value="te">Telugu</SelectItem>
                          </SelectContent>
                        </Select>
                      </div>
                    </div>

                    <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                      <div className="space-y-2">
                        <Label>Channel</Label>
                        <Input value="WhatsApp" readOnly className="bg-slate-50" />
                      </div>
                      <div className="space-y-2">
                        <Label>Category</Label>
                        <Select value={String(draft.category).toLowerCase()} onValueChange={(v) => setDraft((p) => ({ ...p, category: v.toUpperCase() }))}>
                          <SelectTrigger><SelectValue placeholder="Category" /></SelectTrigger>
                          <SelectContent>
                            <SelectItem value="marketing">Marketing</SelectItem>
                            <SelectItem value="utility">Utility</SelectItem>
                            <SelectItem value="authentication">Authentication</SelectItem>
                          </SelectContent>
                        </Select>
                      </div>
                    </div>

                    {draft.channel === "whatsapp" && (
                      <div className="rounded-lg border bg-slate-50 p-3 text-sm space-y-1">
                        <div className={`font-semibold ${guide.color}`}>{guide.title}</div>
                        <div className="text-slate-700">{guide.hint}</div>
                        <div className="text-slate-600">{guide.must}</div>
                      </div>
                    )}

                    <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                      <div className="space-y-2">
                        <Label>Header Type</Label>
                        <Select value={draft.headerType} onValueChange={(v) => setDraft((p) => ({ ...p, headerType: v }))}>
                          <SelectTrigger><SelectValue placeholder="Header" /></SelectTrigger>
                          <SelectContent>
                            <SelectItem value="none">None</SelectItem>
                            <SelectItem value="text">Text</SelectItem>
                            <SelectItem value="image">Image</SelectItem>
                            <SelectItem value="video">Video</SelectItem>
                            <SelectItem value="document">Document</SelectItem>
                          </SelectContent>
                        </Select>
                      </div>
                      <div className="space-y-2 md:col-span-2">
                        <Label>Header Text</Label>
                        <Input placeholder="Optional header text" disabled={isMediaHeader} value={draft.headerText} onChange={(e) => setDraft((p) => ({ ...p, headerText: e.target.value }))} />
                      </div>
                    </div>

                    {isMediaHeader && (
                      <div className="rounded-lg border border-slate-200 p-3 space-y-2">
                        <div className="text-sm font-medium text-slate-700">Header Media ({draft.headerType})</div>
                        <div className="flex flex-wrap items-center gap-2">
                          <Input readOnly value={draft.headerMediaId || "No media uploaded"} className="max-w-lg text-xs" />
                          <label className="inline-flex items-center gap-2">
                            <input type="file" className="hidden" onChange={(e) => uploadHeaderMedia(e.target.files?.[0])} />
                            <Button type="button" variant="outline" disabled={uploading}>
                              <Upload className="w-4 h-4 mr-2" />{uploading ? "Uploading..." : "Upload"}
                            </Button>
                          </label>
                        </div>
                        {draft.headerMediaName ? <div className="text-xs text-slate-500">{draft.headerMediaName}</div> : null}
                      </div>
                    )}

                    <div className="space-y-2">
                      <Label>Message Body</Label>
                      <Textarea className="min-h-[140px]" placeholder="Use {{1}}, {{2}} for variables." value={draft.body} onChange={(e) => setDraft((p) => ({ ...p, body: e.target.value }))} />
                      <p className="text-xs text-slate-500">
                        WhatsApp max 1024 chars. Variables: {templateVars.length ? templateVars.map((v) => `{{${v}}}`).join(", ") : "none"}
                      </p>
                    </div>

                    <div className="space-y-2">
                      <Label>Footer Text</Label>
                      <Input placeholder="Optional footer" value={draft.footerText} onChange={(e) => setDraft((p) => ({ ...p, footerText: e.target.value }))} />
                    </div>

                    <div className="space-y-2">
                      <Label>Buttons JSON (Optional)</Label>
                      <Textarea className="min-h-[90px] font-mono text-xs" placeholder='[{"type":"quick_reply","text":"Track"}]' value={draft.buttonsJson} onChange={(e) => setDraft((p) => ({ ...p, buttonsJson: e.target.value }))} />
                      <p className="text-xs text-slate-500">Allowed: quick_reply, url, phone_number. Avoid URL shorteners.</p>
                    </div>
                  </div>

                  <div className="lg:col-span-2 px-6 py-4 overflow-y-auto max-h-[calc(92vh-170px)] space-y-2">
                    <div className="text-sm font-semibold text-slate-800">Template preview</div>
                    <TemplatePreviewCard template={draft} />
                  </div>
                </div>

                <DialogFooter className="px-6 py-4 border-t bg-white sticky bottom-0">
                  <Button variant="outline" onClick={() => setShowCreateDialog(false)}>Cancel</Button>
                  <Button className="bg-orange-500 hover:bg-orange-600" onClick={createTemplate}>Save Template</Button>
                </DialogFooter>
              </DialogContent>
            </Dialog>
          </div>
        </div>

        <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
          {stats.map((stat, index) => (
            <Card key={index} className={`border-slate-200 ${index === 0 ? "border-orange-500" : ""}`}>
              <CardContent className="pt-6">
                <p className="text-sm text-slate-600">{stat.title}</p>
                <p className="text-2xl font-bold text-slate-900">{stat.value}</p>
              </CardContent>
            </Card>
          ))}
        </div>

        <Card className="border-slate-200">
          <CardHeader>
            <div className="flex items-center justify-between">
              <div className="text-sm text-slate-600">Templates List</div>
              <div className="flex items-center gap-2">
                <div className="relative">
                  <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-400" />
                  <Input placeholder="Search templates..." className="pl-10 w-64" value={tableSearch} onChange={(e) => setTableSearch(e.target.value)} />
                </div>
                <Button variant="outline" size="icon"><Filter className="w-4 h-4" /></Button>
              </div>
            </div>
          </CardHeader>
          <CardContent>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Template</TableHead>
                  <TableHead>Category</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead>Language</TableHead>
                  <TableHead>Updated</TableHead>
                  <TableHead className="w-12"></TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {filteredTemplates.length === 0 ? (
                  <TableRow>
                    <TableCell colSpan={6} className="text-slate-500">
                      No project templates found. Click <b>Sync Meta</b> to fetch WhatsApp templates for this project.
                    </TableCell>
                  </TableRow>
                ) : filteredTemplates.map((template) => (
                  <TableRow key={idValue(template) || nameValue(template)} className="table-row-hover">
                    <TableCell>
                      <div>
                        <p className="font-medium text-slate-900">{nameValue(template)}</p>
                        <p className="text-sm text-slate-500 truncate max-w-xs">{bodyValue(template)}</p>
                      </div>
                    </TableCell>
                    <TableCell>{getCategoryBadge(categoryValue(template))}</TableCell>
                    <TableCell>
                      <div className="space-y-1">
                        {getStatusBadge(statusValue(template))}
                        {(template?.rejectionReason || template?.RejectionReason) && <p className="text-xs text-red-600 flex items-center gap-1"><AlertCircle className="w-3 h-3" />{template?.rejectionReason || template?.RejectionReason}</p>}
                        {String(statusValue(template)).toLowerCase() === "rejected" && !(template?.rejectionReason || template?.RejectionReason) && (
                          <p className="text-xs text-red-600 flex items-center gap-1">
                            <AlertCircle className="w-3 h-3" />
                            Rejected by Meta. Open Preview, fix category/variables/buttons, then re-submit.
                          </p>
                        )}
                      </div>
                    </TableCell>
                    <TableCell className="text-slate-600 text-sm">{languageValue(template)}</TableCell>
                    <TableCell className="text-slate-600 text-sm">{template?.updatedAtUtc || template?.UpdatedAtUtc ? new Date(template?.updatedAtUtc || template?.UpdatedAtUtc).toLocaleDateString() : "-"}</TableCell>
                    <TableCell>
                      <DropdownMenu>
                        <DropdownMenuTrigger asChild>
                          <Button variant="ghost" size="icon"><MoreVertical className="w-4 h-4" /></Button>
                        </DropdownMenuTrigger>
                        <DropdownMenuContent align="end">
                          <DropdownMenuItem onClick={() => { setPreviewTemplate(template); setShowPreviewDialog(true); }}>
                            <FileText className="w-4 h-4 mr-2" />Preview
                          </DropdownMenuItem>
                          {channelValue(template) === 2 && (
                            <DropdownMenuItem onClick={() => submitTemplateToMeta(idValue(template))}>
                              <Send className="w-4 h-4 mr-2" />Submit to Meta
                            </DropdownMenuItem>
                          )}
                          <DropdownMenuSeparator />
                          <DropdownMenuItem className="text-red-600" onClick={() => setDeleteTarget(template)}>
                            <Trash2 className="w-4 h-4 mr-2" />Delete
                          </DropdownMenuItem>
                        </DropdownMenuContent>
                      </DropdownMenu>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </CardContent>
        </Card>
      </div>

      <Dialog open={showPreviewDialog} onOpenChange={setShowPreviewDialog}>
        <DialogContent className="max-w-xl">
          <DialogHeader>
            <DialogTitle>{previewTemplate?.name || "Template preview"}</DialogTitle>
            <DialogDescription>
              {previewTemplate ? `${previewTemplate.category || ""} · ${previewTemplate.language || "en"}` : ""}
            </DialogDescription>
          </DialogHeader>
          <TemplatePreviewCard template={previewTemplate || {}} />
        </DialogContent>
      </Dialog>

      <AlertDialog open={!!deleteTarget} onOpenChange={(open) => !open && setDeleteTarget(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Delete template?</AlertDialogTitle>
            <AlertDialogDescription>
              This will delete the WhatsApp template from Meta and from your project database. This action cannot be undone.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={deleteBusy}>Cancel</AlertDialogCancel>
            <AlertDialogAction className="bg-red-600 hover:bg-red-700" onClick={removeTemplate} disabled={deleteBusy}>
              {deleteBusy ? "Deleting..." : "Delete"}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  );
};

export default TemplatesPage;
