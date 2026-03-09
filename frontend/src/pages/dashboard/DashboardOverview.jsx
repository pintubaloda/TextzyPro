import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Progress } from "@/components/ui/progress";
import {
  MessageSquare,
  Send,
  Users,
  ArrowRight,
  MessageCircle,
  Plus,
  Calendar,
  MoreVertical,
  QrCode,
  PhoneCall,
  Bot,
  Plug,
} from "lucide-react";
import { BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer } from "recharts";
import { useCallback, useEffect, useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import { apiGet, authProjects, getSession, getTenantWebhookAnalytics, wabaExchangeCode, wabaGetEmbeddedConfig, wabaGetOnboardingStatus, wabaMapExisting, wabaStartOnboarding } from "@/lib/api";
import { loadFacebookSdk } from "@/lib/facebookSdk";
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from "@/components/ui/dropdown-menu";
import { toast } from "sonner";
import { Input } from "@/components/ui/input";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";

const DashboardOverview = () => {
  const navigate = useNavigate();
  const session = getSession();
  const [messages, setMessages] = useState([]);
  const [contacts, setContacts] = useState([]);
  const [campaigns, setCampaigns] = useState([]);
  const [recentInboxRows, setRecentInboxRows] = useState([]);
  const [wabaStatus, setWabaStatus] = useState({ state: "requested", isConnected: false, businessName: "", phone: "" });
  const [connectingWaba, setConnectingWaba] = useState(false);
  const [reusingWaba, setReusingWaba] = useState(false);
  const [mapDialogOpen, setMapDialogOpen] = useState(false);
  const [projects, setProjects] = useState([]);
  const [mapForm, setMapForm] = useState({
    tenantId: "",
    wabaId: "",
    phoneNumberId: "",
    accessToken: "",
  });
  const [webhookAnalytics, setWebhookAnalytics] = useState(null);
  const [analyticsDays, setAnalyticsDays] = useState(7);
  const [embeddedCfg, setEmbeddedCfg] = useState({
    appId: process.env.REACT_APP_FACEBOOK_APP_ID || "",
    configId: process.env.REACT_APP_WABA_EMBEDDED_CONFIG_ID || "",
  });
  const wabaConnected = useMemo(() => {
    const state = String(wabaStatus?.state || "").toLowerCase();
    return !!wabaStatus?.isConnected || !!wabaStatus?.readyToSend || state === "ready";
  }, [wabaStatus]);

  const displayValue = useCallback((value, fallback = "Not available") => {
    const raw = String(value ?? "").trim();
    return raw ? raw : fallback;
  }, []);

  const ensureEmbeddedConfig = useCallback(async () => {
    if (embeddedCfg.appId && embeddedCfg.configId) return;
    try {
      const cfg = await wabaGetEmbeddedConfig();
      if (cfg?.appId || cfg?.embeddedConfigId) {
        setEmbeddedCfg((prev) => ({
          appId: prev.appId || (cfg.appId || ""),
          configId: prev.configId || (cfg.embeddedConfigId || ""),
        }));
      }
    } catch {
      // Keep env-based values only.
    }
  }, [embeddedCfg.appId, embeddedCfg.configId]);

  const resolveEmbeddedConfig = async () => {
    let appId = embeddedCfg.appId;
    let configId = embeddedCfg.configId;
    if (appId && configId) return { appId, configId };
    try {
      const cfg = await wabaGetEmbeddedConfig();
      appId = appId || (cfg?.appId || "");
      configId = configId || (cfg?.embeddedConfigId || "");
      setEmbeddedCfg({ appId, configId });
    } catch {
      // noop
    }
    return { appId, configId };
  };

  const loadWabaStatus = useCallback(async (force = false) => {
    try {
      const data = await wabaGetOnboardingStatus({ force });
      setWabaStatus(data || { state: "requested", isConnected: false, businessName: "", phone: "" });
    } catch {
      setWabaStatus({ state: "requested", isConnected: false, businessName: "", phone: "" });
    }
  }, []);

  const loadWebhookAnalytics = useCallback(async (days) => {
    try {
      const an = await getTenantWebhookAnalytics(days);
      setWebhookAnalytics(an || null);
    } catch {
      setWebhookAnalytics(null);
    }
  }, []);

  useEffect(() => {
    Promise.all([
      apiGet("/api/messages"),
      apiGet("/api/contacts"),
      apiGet("/api/campaigns"),
      apiGet("/api/inbox/conversations?take=5").catch(() => []),
    ])
      .then(([m, c, cp, conv]) => {
        setMessages(m || []);
        setContacts(c || []);
        setCampaigns(cp || []);
        setRecentInboxRows(Array.isArray(conv) ? conv : []);
      })
      .catch(() => {});
    loadWabaStatus(true);
    loadWebhookAnalytics(7);
    ensureEmbeddedConfig();
  }, [ensureEmbeddedConfig, loadWabaStatus, loadWebhookAnalytics]);

  const handleEmbeddedConnect = async () => {
    const { appId: facebookAppId, configId: embeddedConfigId } = await resolveEmbeddedConfig();
    if (!facebookAppId || !embeddedConfigId) {
      toast.error("Missing Facebook App ID or Embedded Config ID in Platform WABA Master Config");
      return;
    }

    setConnectingWaba(true);
    try {
      await wabaStartOnboarding();
      const FB = await loadFacebookSdk(facebookAppId);
      FB.login((response) => {
        if (!response || !response.authResponse) {
          setConnectingWaba(false);
          toast.error("Embedded signup cancelled");
          return;
        }

        const code = response.authResponse.code;
        if (!code) {
          setConnectingWaba(false);
          toast.error("Meta did not return authorization code");
          return;
        }

        Promise.resolve()
          .then(() => wabaExchangeCode(code))
          .then(() => loadWabaStatus(true))
          .then(() => toast.success("WhatsApp onboarding connected"))
          .catch((e) => {
            toast.error(e?.message || "Failed to exchange embedded signup code");
          })
          .finally(() => setConnectingWaba(false));
      }, {
        config_id: embeddedConfigId,
        response_type: "code",
        override_default_response_type: true,
        scope: "business_management,whatsapp_business_management,whatsapp_business_messaging",
      });
    } catch (e) {
      setConnectingWaba(false);
      toast.error(e.message || "Unable to start onboarding");
    }
  };

  const handleMapExistingWaba = async () => {
    setReusingWaba(true);
    try {
      const list = await authProjects();
      const rows = Array.isArray(list) ? list : [];
      const session = getSession();
      const selected = rows.find((x) => x.slug === session?.tenantSlug) || rows[0];
      setProjects(rows);
      setMapForm((prev) => ({
        ...prev,
        tenantId: selected?.id || prev.tenantId || "",
      }));
      setMapDialogOpen(true);
    } catch (e) {
      toast.error(e?.message || "Failed to load projects");
    } finally {
      setReusingWaba(false);
    }
  };

  const submitMapExisting = async () => {
    if (!mapForm.tenantId) {
      toast.error("Select project");
      return;
    }
    if (!mapForm.wabaId.trim() || !mapForm.phoneNumberId.trim() || !mapForm.accessToken.trim()) {
      toast.error("Project, WABA ID, Phone Number ID and Access Token are required");
      return;
    }

    setReusingWaba(true);
    try {
      await wabaMapExisting({
        tenantId: mapForm.tenantId,
        wabaId: mapForm.wabaId.trim(),
        phoneNumberId: mapForm.phoneNumberId.trim(),
        accessToken: mapForm.accessToken.trim(),
      });
      setMapDialogOpen(false);
      setMapForm((prev) => ({ ...prev, wabaId: "", phoneNumberId: "", accessToken: "" }));
      await loadWabaStatus(true);
      toast.success("Existing WABA mapped to selected project");
    } catch (e) {
      toast.error(e?.message || "Failed to map existing WABA");
    } finally {
      setReusingWaba(false);
    }
  };

  const computedStats = useMemo(() => {
    const total = messages.length;
    const sent = messages.filter((x) => x.status === "Accepted").length;
    const wa = messages.filter((x) => x.channel === 2).length;
    const sms = messages.filter((x) => x.channel === 1).length;
    return { total, sent, wa, sms, contacts: contacts.length };
  }, [messages, contacts]);

  const stats = [
    {
      title: "Total Messages",
      value: computedStats.total.toLocaleString(),
      icon: MessageSquare,
      color: "orange",
    },
    {
      title: "WhatsApp Sent",
      value: computedStats.wa.toLocaleString(),
      icon: MessageCircle,
      color: "green",
    },
    {
      title: "SMS Sent",
      value: computedStats.sms.toLocaleString(),
      icon: Send,
      color: "blue",
    },
    {
      title: "Active Contacts",
      value: computedStats.contacts.toLocaleString(),
      icon: Users,
      color: "purple",
    },
  ];

  const recentCampaigns = (campaigns || []).slice(0, 3).map((c) => ({
    id: c.id || c.Id,
    name: c.name || c.Name || "Untitled Campaign",
    channel: String(c.channel ?? c.Channel ?? "").toLowerCase(),
    status: String(c.status || c.Status || "active").toLowerCase(),
    sent: Number(c.sent ?? c.Sent ?? 0),
    delivered: Number(c.delivered ?? c.Delivered ?? 0),
    read: Number(c.read ?? c.Read ?? 0),
    failed: Number(c.failed ?? c.Failed ?? 0),
    createdAtUtc: c.createdAtUtc || c.CreatedAtUtc || null,
  }));

  const formatAgo = (value) => {
    if (!value) return "-";
    const time = new Date(value).getTime();
    if (!Number.isFinite(time)) return "-";
    const diffMs = Date.now() - time;
    const diffMin = Math.max(1, Math.floor(diffMs / 60000));
    if (diffMin < 60) return `${diffMin} min ago`;
    const diffH = Math.floor(diffMin / 60);
    if (diffH < 24) return `${diffH} hour${diffH > 1 ? "s" : ""} ago`;
    const diffD = Math.floor(diffH / 24);
    return `${diffD} day${diffD > 1 ? "s" : ""} ago`;
  };

  const recentConversations = (recentInboxRows || []).slice(0, 5).map((row) => ({
    id: row.id || row.Id,
    name: row.customerName || row.CustomerName || row.name || "Customer",
    phone: row.customerPhone || row.CustomerPhone || row.phone || "-",
    message: row.lastMessagePreview || row.LastMessagePreview || row.lastMessage || "Open conversation",
    time: formatAgo(row.lastInboundAtUtc || row.LastInboundAtUtc || row.lastMessageAtUtc || row.LastMessageAtUtc || row.updatedAtUtc || row.UpdatedAtUtc),
    unread: Number(row.unreadCount || row.UnreadCount || row.unread || 0) > 0,
  }));

  const webhookStatusData = useMemo(() => {
    const map = webhookAnalytics?.statusSummary || {};
    return ["AcceptedByMeta", "Sent", "Delivered", "Read", "Failed"].map((k) => ({
      name: k.replace("ByMeta", ""),
      count: Number(map[k] || map[k.toLowerCase()] || 0),
    }));
  }, [webhookAnalytics]);

  const webhookFailureData = useMemo(() => {
    const map = webhookAnalytics?.failureCodes || {};
    return Object.keys(map)
      .slice(0, 6)
      .map((k) => ({ name: k, count: Number(map[k] || 0) }));
  }, [webhookAnalytics]);

  const wabaChatLink = useMemo(() => {
    const raw = (wabaStatus?.phone || "").trim();
    const digits = raw.replace(/[^\d]/g, "");
    if (!digits) return "";
    return `https://wa.me/${digits}`;
  }, [wabaStatus?.phone]);

  const wabaQrUrl = useMemo(() => {
    if (!wabaChatLink) return "";
    return `https://api.qrserver.com/v1/create-qr-code/?size=260x260&data=${encodeURIComponent(wabaChatLink)}`;
  }, [wabaChatLink]);

  const getStatusBadge = (status) => {
    switch (status) {
      case "completed":
        return <Badge className="bg-green-100 text-green-700 hover:bg-green-100">Completed</Badge>;
      case "active":
        return <Badge className="bg-blue-100 text-blue-700 hover:bg-blue-100">Active</Badge>;
      case "scheduled":
        return <Badge className="bg-yellow-100 text-yellow-700 hover:bg-yellow-100">Scheduled</Badge>;
      default:
        return null;
    }
  };

  return (
    <div className="space-y-6" data-testid="dashboard-overview">
      {/* Header */}
      <div className="flex flex-col md:flex-row md:items-center md:justify-between gap-4">
        <div>
          <h1 className="text-2xl font-heading font-bold text-slate-900">Dashboard</h1>
          <p className="text-slate-600">Project {displayValue(session.projectName || session.tenantSlug, "workspace")} live overview and activity.</p>
        </div>
        <div className="flex items-center gap-3">
          <Button variant="outline" className="gap-2" data-testid="date-range-btn">
            <Calendar className="w-4 h-4" />
            Last 7 days
          </Button>
          <Button className="bg-orange-500 hover:bg-orange-600 text-white gap-2" data-testid="new-campaign-btn">
            <Plus className="w-4 h-4" />
            New Campaign
          </Button>
        </div>
      </div>

      {/* KPI Cards - Top */}
      <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-4 gap-4">
        {stats.map((stat, index) => (
          <Card key={index} className="relative overflow-hidden border-slate-200 bg-white shadow-sm" data-testid={`stat-card-${index}`}>
            <div className="absolute -top-12 -right-12 w-36 h-36 rounded-full bg-slate-100/80" />
            <CardContent className="pt-4 pb-4 relative z-10">
              <div className="flex items-start justify-between">
                <div>
                  <p className="text-lg text-slate-600 mb-1">{stat.title}</p>
                  <p className="text-5xl leading-none font-bold text-slate-900 mt-1">{stat.value}</p>
                  <div className="flex items-center gap-1 mt-3 text-sm text-slate-500">
                    Live tenant data
                  </div>
                </div>
                <div className={`w-14 h-14 rounded-xl flex items-center justify-center ${
                  stat.color === "orange" ? "bg-orange-100" :
                  stat.color === "green" ? "bg-green-100" :
                  stat.color === "blue" ? "bg-blue-100" : "bg-purple-100"
                }`}>
                  <stat.icon className={`w-6 h-6 ${
                    stat.color === "orange" ? "text-orange-500" :
                    stat.color === "green" ? "text-green-500" :
                    stat.color === "blue" ? "text-blue-500" : "text-purple-500"
                  }`} />
                </div>
              </div>
            </CardContent>
          </Card>
        ))}
      </div>

      <section className="rounded-[24px] p-6 md:p-8 text-slate-900 border border-slate-200 shadow-sm bg-gradient-to-br from-white via-slate-50 to-orange-50/50 relative overflow-hidden">
        <div className="absolute -top-16 -right-16 h-44 w-44 rounded-full bg-orange-100/50 blur-2xl" />
        <div className="absolute -bottom-16 -left-16 h-44 w-44 rounded-full bg-blue-100/30 blur-2xl" />
        <div className="space-y-6 relative z-10">
          <div className="flex flex-wrap items-center gap-3 text-sm">
            <div className="px-5 py-2 rounded-full bg-white border border-slate-200 text-slate-700 shadow-sm">WhatsApp Business API Status: <b className={wabaConnected ? "text-green-600" : "text-orange-600"}>{wabaConnected ? "Connected" : "Setup Required"}</b></div>
            {!wabaConnected ? (
              <Button className="rounded-full bg-orange-500 hover:bg-orange-600 text-white shadow-md shadow-orange-500/25" onClick={handleEmbeddedConnect} disabled={connectingWaba}>
                {connectingWaba ? "Connecting..." : "Apply Now"}
              </Button>
            ) : null}
            <div className="px-5 py-2 rounded-full bg-white/90 border border-slate-200 text-slate-700 shadow-sm">TRAIL(Pro + Flows)</div>
          </div>

          <div className="grid lg:grid-cols-3 gap-4">
            <div className="lg:col-span-2 rounded-2xl border border-slate-200 bg-white p-6 shadow-sm">
              <h3 className="text-3xl font-heading font-semibold leading-tight mb-4 text-slate-900">
                {wabaConnected ? "WhatsApp Business Account Connected" : "Setup FREE WhatsApp Business Account"}
              </h3>
              <div className="rounded-xl border border-slate-200 bg-gradient-to-r from-white to-orange-50 p-4 mb-5 text-base text-slate-700">
                {wabaConnected ? "Connection is active for this project." : "Apply for WhatsApp Business API"}
              </div>
              <p className="text-slate-600 mb-2 text-lg leading-snug">
                {wabaConnected
                  ? "Your WhatsApp Cloud API onboarding is complete. You can now send and receive messages."
                  : "Click on Continue with Facebook to apply for WhatsApp Business API"}
              </p>
              <p className="text-slate-600 text-lg leading-snug">
                {wabaConnected ? "Connection details below are synced from your WhatsApp Cloud API onboarding." : "Requirement: Registered Business & Working Website."}
              </p>
              {!wabaConnected ? (
                <>
                  <div className="mt-4 flex flex-wrap gap-3">
                    <Button variant="outline" className="border-slate-300 text-slate-700 bg-white hover:bg-slate-100 text-base px-7">Schedule Meeting</Button>
                    <Button className="bg-orange-500 hover:bg-orange-600 text-white text-base px-7 shadow-md shadow-orange-500/25" onClick={handleEmbeddedConnect} disabled={connectingWaba}>
                      {connectingWaba ? "Connecting..." : "Continue with Facebook"}
                    </Button>
                  </div>
                  <div className="mt-3">
                    <Button
                      className="bg-orange-500 hover:bg-orange-600 text-white text-base px-7 shadow-md shadow-orange-500/25"
                      onClick={handleMapExistingWaba}
                      disabled={reusingWaba}
                    >
                      {reusingWaba ? "Mapping..." : "Map Existing WABA"}
                    </Button>
                    <p className="text-sm text-slate-500 mt-2 leading-relaxed max-w-2xl">
                      If you are already using WhatsApp Business, click here to connect your existing account to this project
                      without creating a new WABA.
                    </p>
                  </div>
                </>
              ) : (
                <div className="mt-4 grid sm:grid-cols-2 gap-3">
                  <div className="rounded-xl border border-slate-200 bg-slate-50 p-3">
                    <p className="text-xs text-slate-500">Connection Health</p>
                    <p className="text-sm font-semibold text-slate-900">
                      {wabaStatus.accountHealth || (wabaStatus.permissionAuditPassed && wabaStatus.webhookSubscribed ? "Healthy" : "Needs Attention")}
                    </p>
                  </div>
                  <div className="rounded-xl border border-slate-200 bg-slate-50 p-3">
                    <p className="text-xs text-slate-500">Display Number</p>
                    <p className="text-sm font-semibold text-slate-900">{wabaStatus.phone || "-"}</p>
                  </div>
                  <div className="rounded-xl border border-slate-200 bg-slate-50 p-3">
                    <p className="text-xs text-slate-500">Business Name</p>
                    <p className="text-sm font-semibold text-slate-900">{wabaStatus.businessName || "-"}</p>
                  </div>
                  <div className="rounded-xl border border-slate-200 bg-slate-50 p-3">
                    <p className="text-xs text-slate-500">Quality</p>
                    <p className="text-sm font-semibold text-slate-900">{displayValue(wabaStatus.phoneQualityRating)}</p>
                  </div>
                  <div className="rounded-xl border border-slate-200 bg-slate-50 p-3">
                    <p className="text-xs text-slate-500">Messaging Limit Tier</p>
                    <p className="text-sm font-semibold text-slate-900">{displayValue(wabaStatus.messagingLimitTier)}</p>
                  </div>
                  <div className="rounded-xl border border-slate-200 bg-slate-50 p-3">
                    <p className="text-xs text-slate-500">Account Health</p>
                    <p className="text-sm font-semibold text-slate-900">{displayValue(wabaStatus.accountHealth)}</p>
                  </div>
                </div>
              )}
            </div>
            <div className="rounded-2xl border border-slate-200 bg-white p-6 text-center shadow-sm">
              <div className="w-36 h-36 mx-auto mb-4 rounded-2xl bg-slate-50 border border-slate-200 flex items-center justify-center overflow-hidden">
                {wabaQrUrl ? (
                  <img src={wabaQrUrl} alt="WhatsApp QR" className="h-full w-full object-cover" />
                ) : (
                  <QrCode className="w-24 h-24 text-slate-700" />
                )}
              </div>
              <p className="text-3xl font-semibold leading-tight">{displayValue(wabaStatus.businessName, displayValue(session.projectName, "Business not linked"))}</p>
              <p className="text-slate-600 mt-2 text-xl">{displayValue(wabaStatus.phone, "Phone number not linked")}</p>
              <p className="text-sm text-slate-500 mt-2">{wabaConnected ? "Connected / Ready" : displayValue(wabaStatus.state, "Awaiting setup")}</p>
              <div className="mt-3 rounded-xl border border-slate-200 bg-slate-50 p-3 text-left text-xs space-y-1">
                <div className="flex justify-between gap-2"><span className="text-slate-500">WABA ID</span><span className="text-slate-900 font-medium break-all">{displayValue(wabaStatus.wabaId)}</span></div>
                <div className="flex justify-between gap-2"><span className="text-slate-500">Phone Number ID</span><span className="text-slate-900 font-medium break-all">{displayValue(wabaStatus.phoneNumberId)}</span></div>
                <div className="flex justify-between gap-2"><span className="text-slate-500">Business Manager ID</span><span className="text-slate-900 font-medium break-all">{displayValue(wabaStatus.businessManagerId)}</span></div>
                <div className="flex justify-between gap-2"><span className="text-slate-500">Token Source</span><span className="text-slate-900 font-medium">{displayValue(wabaStatus.tokenSource)}</span></div>
                <div className="flex justify-between gap-2"><span className="text-slate-500">Messaging Limit Tier</span><span className="text-slate-900 font-medium">{displayValue(wabaStatus.messagingLimitTier)}</span></div>
                <div className="flex justify-between gap-2"><span className="text-slate-500">Account Health</span><span className="text-slate-900 font-medium">{displayValue(wabaStatus.accountHealth)}</span></div>
              </div>
            </div>
          </div>

          <div className="grid md:grid-cols-2 xl:grid-cols-4 gap-3">
            <button onClick={() => navigate("/dashboard/contacts")} className="rounded-2xl border border-slate-200 bg-white p-4 text-left text-lg hover:shadow-sm transition"><PhoneCall className="w-6 h-6 mb-2 text-orange-500" />Add WhatsApp Contacts</button>
            <button onClick={() => navigate("/dashboard/team")} className="rounded-2xl border border-slate-200 bg-white p-4 text-left text-lg hover:shadow-sm transition"><Users className="w-6 h-6 mb-2 text-orange-500" />Add Team Members</button>
            <button onClick={() => navigate("/dashboard/integrations")} className="rounded-2xl border border-slate-200 bg-white p-4 text-left text-lg hover:shadow-sm transition"><Plug className="w-6 h-6 mb-2 text-orange-500" />Explore Integrations</button>
            <button onClick={() => navigate("/dashboard/automations")} className="rounded-2xl border border-slate-200 bg-white p-4 text-left text-lg hover:shadow-sm transition"><Bot className="w-6 h-6 mb-2 text-orange-500" />Chatbot Setup</button>
          </div>
        </div>
      </section>

      {/* Bottom Row */}
      <div className="grid lg:grid-cols-2 gap-6">
        {/* Recent Campaigns */}
        <Card className="border-slate-200" data-testid="recent-campaigns">
          <CardHeader>
            <div className="flex items-center justify-between">
              <div>
                <CardTitle>Recent Campaigns</CardTitle>
                <CardDescription>Your latest campaign performance</CardDescription>
              </div>
              <Button variant="ghost" size="sm" className="text-orange-500 hover:text-orange-600" data-testid="view-all-campaigns-btn" onClick={() => navigate("/dashboard/campaigns")}>
                View All <ArrowRight className="w-4 h-4 ml-1" />
              </Button>
            </div>
          </CardHeader>
          <CardContent>
            <div className="space-y-4">
              {recentCampaigns.length === 0 ? (
                <div className="rounded-lg border border-dashed border-slate-200 bg-slate-50 p-6 text-sm text-slate-500">
                  No campaigns created yet.
                </div>
              ) : null}
              {recentCampaigns.map((campaign, index) => (
                <div
                  key={campaign.id || index}
                  className="flex items-center justify-between p-4 bg-slate-50 rounded-lg hover:bg-slate-100 transition-colors"
                  data-testid={`campaign-item-${index}`}
                >
                  <div className="flex-1">
                    <div className="flex items-center gap-3 mb-2">
                      <p className="font-medium text-slate-900">{campaign.name}</p>
                      {getStatusBadge(campaign.status)}
                      <Badge variant="outline" className="text-xs">{campaign.channel || "campaign"}</Badge>
                    </div>
                    <div className="flex items-center gap-4 text-sm text-slate-500">
                      <span>Sent: {campaign.sent.toLocaleString()}</span>
                      <span>Delivered: {campaign.delivered.toLocaleString()}</span>
                      <span>Read: {campaign.read.toLocaleString()}</span>
                      <span>Created: {campaign.createdAtUtc ? new Date(campaign.createdAtUtc).toLocaleDateString() : "-"}</span>
                    </div>
                  </div>
                  <Button variant="ghost" size="icon">
                    <MoreVertical className="w-4 h-4" />
                  </Button>
                </div>
              ))}
            </div>
          </CardContent>
        </Card>

        {/* Recent Conversations */}
        <Card className="border-slate-200" data-testid="recent-conversations">
          <CardHeader>
            <div className="flex items-center justify-between">
              <div>
                <CardTitle>Recent Conversations</CardTitle>
                <CardDescription>Latest messages from customers</CardDescription>
              </div>
              <Button variant="ghost" size="sm" className="text-orange-500 hover:text-orange-600" data-testid="view-all-conversations-btn" onClick={() => navigate("/dashboard/inbox")}>
                View All <ArrowRight className="w-4 h-4 ml-1" />
              </Button>
            </div>
          </CardHeader>
          <CardContent>
            <div className="space-y-4">
              {recentConversations.length === 0 ? (
                <div className="rounded-lg border border-dashed border-slate-200 bg-slate-50 p-6 text-sm text-slate-500">
                  No conversations yet.
                </div>
              ) : null}
              {recentConversations.map((conversation, index) => (
                <div
                  key={conversation.id || index}
                  className="flex items-start gap-4 p-4 bg-slate-50 rounded-lg hover:bg-slate-100 transition-colors cursor-pointer"
                  onClick={() => navigate("/dashboard/inbox")}
                  data-testid={`conversation-item-${index}`}
                >
                  <div className="w-10 h-10 rounded-full bg-gradient-to-br from-orange-400 to-orange-600 flex items-center justify-center text-white font-medium flex-shrink-0">
                    {conversation.name.split(" ").map(n => n[0]).join("")}
                  </div>
                  <div className="flex-1 min-w-0">
                    <div className="flex items-center justify-between mb-1">
                      <p className="font-medium text-slate-900">{conversation.name}</p>
                      <span className="text-xs text-slate-500">{conversation.time}</span>
                    </div>
                    <p className="text-sm text-slate-500 truncate">{conversation.message}</p>
                  </div>
                  {conversation.unread && (
                    <div className="w-2 h-2 bg-orange-500 rounded-full mt-2 flex-shrink-0"></div>
                  )}
                </div>
              ))}
            </div>
          </CardContent>
        </Card>
      </div>

      <div className="grid lg:grid-cols-2 gap-6">
        <Card className="border-slate-200">
          <CardHeader>
            <div className="flex items-center justify-between gap-3">
              <div>
                <CardTitle>Webhook Status Analytics</CardTitle>
                <CardDescription>Tenant-level outbound status in last {analyticsDays} days</CardDescription>
              </div>
              <select
                className="h-9 rounded-md border border-slate-200 bg-white px-2 text-sm"
                value={analyticsDays}
                onChange={(e) => {
                  const d = Number(e.target.value);
                  setAnalyticsDays(d);
                  loadWebhookAnalytics(d);
                }}
              >
                <option value={7}>7d</option>
                <option value={15}>15d</option>
                <option value={30}>30d</option>
              </select>
            </div>
          </CardHeader>
          <CardContent>
            <div className="h-[260px]">
              <ResponsiveContainer width="100%" height="100%">
                <BarChart data={webhookStatusData}>
                  <CartesianGrid strokeDasharray="3 3" stroke="#E2E8F0" />
                  <XAxis dataKey="name" stroke="#64748B" fontSize={12} />
                  <YAxis stroke="#64748B" fontSize={12} />
                  <Tooltip />
                  <Bar dataKey="count" fill="#f97316" radius={[6, 6, 0, 0]} />
                </BarChart>
              </ResponsiveContainer>
            </div>
          </CardContent>
        </Card>

        <Card className="border-slate-200">
          <CardHeader>
            <CardTitle>Failure Code Diagnostics</CardTitle>
            <CardDescription>Top webhook/message failure codes for this tenant</CardDescription>
          </CardHeader>
          <CardContent>
            <div className="space-y-3">
              {!webhookFailureData.length && <p className="text-sm text-slate-500">No recent failure codes.</p>}
              {webhookFailureData.map((row) => (
                <div key={row.name} className="flex items-center justify-between rounded-md border border-slate-200 px-3 py-2">
                  <span className="text-sm font-medium text-slate-700">{row.name}</span>
                  <Badge variant="secondary" className="bg-orange-100 text-orange-700">{row.count}</Badge>
                </div>
              ))}
            </div>
          </CardContent>
        </Card>
      </div>

      <Dialog open={mapDialogOpen} onOpenChange={setMapDialogOpen}>
        <DialogContent className="sm:max-w-xl">
          <DialogHeader>
            <DialogTitle>Map Existing WABA</DialogTitle>
            <DialogDescription>
              Enter existing WhatsApp Cloud credentials and map them to a project.
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-3">
            <div>
              <label className="text-sm font-medium text-slate-700">Select Project</label>
              <select
                className="mt-1 h-10 w-full rounded-md border border-slate-200 bg-white px-3 text-sm"
                value={mapForm.tenantId}
                onChange={(e) => setMapForm((prev) => ({ ...prev, tenantId: e.target.value }))}
              >
                <option value="">Select project</option>
                {projects.map((p) => (
                  <option key={p.id} value={p.id}>
                    {p.name}
                  </option>
                ))}
              </select>
            </div>
            <div>
              <label className="text-sm font-medium text-slate-700">WABA ID</label>
              <Input
                placeholder="Enter WABA ID"
                value={mapForm.wabaId}
                onChange={(e) => setMapForm((prev) => ({ ...prev, wabaId: e.target.value }))}
              />
            </div>
            <div>
              <label className="text-sm font-medium text-slate-700">Phone Number ID</label>
              <Input
                placeholder="Enter Phone Number ID"
                value={mapForm.phoneNumberId}
                onChange={(e) => setMapForm((prev) => ({ ...prev, phoneNumberId: e.target.value }))}
              />
            </div>
            <div>
              <label className="text-sm font-medium text-slate-700">Access Token</label>
              <Input
                type="password"
                placeholder="Enter Access Token"
                value={mapForm.accessToken}
                onChange={(e) => setMapForm((prev) => ({ ...prev, accessToken: e.target.value }))}
              />
            </div>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setMapDialogOpen(false)} disabled={reusingWaba}>
              Cancel
            </Button>
            <Button
              className="bg-orange-500 hover:bg-orange-600 text-white"
              onClick={submitMapExisting}
              disabled={reusingWaba}
            >
              {reusingWaba ? "Mapping..." : "Map Existing WABA"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
};

export default DashboardOverview;
