import { useEffect, useMemo, useState } from "react";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  BarChart3,
  Building2,
  CheckCheck,
  ContactRound,
  CreditCard,
  MessageSquare,
  RefreshCcw,
  ShieldAlert,
  Smartphone,
  TrendingUp,
  Users,
} from "lucide-react";
import {
  ResponsiveContainer,
  BarChart,
  Bar,
  CartesianGrid,
  Tooltip,
  XAxis,
  YAxis,
  PieChart,
  Pie,
  Cell,
  Legend,
  LineChart,
  Line,
} from "recharts";
import {
  getPlatformCustomers,
  getPlatformMobileTelemetry,
  getPlatformQueueHealth,
  getPlatformSecuritySignals,
  getPlatformWebhookAnalytics,
  getSession,
  getTenantAnalyticsOverview,
  getTenantWebhookAnalytics,
} from "@/lib/api";
import { toast } from "sonner";

const COLORS = ["#f97316", "#22c55e", "#3b82f6", "#8b5cf6", "#eab308", "#ef4444", "#64748b"];
const money = (value) => `INR ${Number(value || 0).toLocaleString()}`;
const number = (value) => Number(value || 0).toLocaleString();

function KpiCard({ title, value, hint, icon: Icon, tone = "orange" }) {
  const tones = {
    orange: "bg-orange-50 text-orange-600",
    green: "bg-emerald-50 text-emerald-600",
    blue: "bg-blue-50 text-blue-600",
    purple: "bg-violet-50 text-violet-600",
    rose: "bg-rose-50 text-rose-600",
    slate: "bg-slate-100 text-slate-600",
  };
  return (
    <Card className="border-slate-200 shadow-sm">
      <CardContent className="pt-6">
        <div className="flex items-start justify-between gap-4">
          <div>
            <p className="text-sm text-slate-500">{title}</p>
            <p className="mt-2 text-3xl font-bold text-slate-950">{value}</p>
            <p className="mt-2 text-sm text-slate-500">{hint}</p>
          </div>
          <div className={`inline-flex h-11 w-11 items-center justify-center rounded-2xl ${tones[tone] || tones.orange}`}>
            <Icon className="h-5 w-5" />
          </div>
        </div>
      </CardContent>
    </Card>
  );
}

function TenantAnalytics({ days }) {
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [overview, setOverview] = useState(null);
  const [webhook, setWebhook] = useState(null);

  const load = async (showSpinner = true) => {
    if (showSpinner) setLoading(true);
    else setRefreshing(true);
    try {
      const [overviewRes, webhookRes] = await Promise.all([
        getTenantAnalyticsOverview(days).catch(() => null),
        getTenantWebhookAnalytics(days).catch(() => null),
      ]);
      setOverview(overviewRes || null);
      setWebhook(webhookRes || null);
    } catch (e) {
      toast.error(e?.message || "Failed to load analytics");
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  };

  useEffect(() => {
    load(true);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [days]);

  const statusRows = useMemo(() => {
    const rows = Object.entries(webhook?.statusSummary || {}).map(([name, value]) => ({ name, value: Number(value || 0) }));
    return rows.length ? rows : (overview?.statusDistribution || []);
  }, [overview, webhook]);

  const failureRows = useMemo(() => {
    return Object.entries(webhook?.failureCodes || {})
      .map(([name, value]) => ({ name, value: Number(value || 0) }))
      .sort((a, b) => b.value - a.value)
      .slice(0, 8);
  }, [webhook]);

  const cards = useMemo(() => {
    const totals = overview?.totals || {};
    const rates = overview?.rates || {};
    return [
      {
        title: "Messages",
        value: number(totals.totalMessages),
        hint: `Last ${days} days`,
        icon: MessageSquare,
        tone: "orange",
      },
      {
        title: "Delivery Rate",
        value: `${Number(rates.deliveryRate || 0).toFixed(1)}%`,
        hint: `${number(totals.delivered)} delivered`,
        icon: CheckCheck,
        tone: "green",
      },
      {
        title: "Read Rate",
        value: `${Number(rates.readRate || 0).toFixed(1)}%`,
        hint: `${number(totals.read)} read`,
        icon: TrendingUp,
        tone: "blue",
      },
      {
        title: "Contacts",
        value: number(totals.totalContacts),
        hint: `${number(totals.activeConversations)} active conversations`,
        icon: ContactRound,
        tone: "purple",
      },
    ];
  }, [overview, days]);

  if (loading) {
    return <div className="rounded-3xl border border-slate-200 bg-white p-10 text-sm text-slate-500">Loading analytics...</div>;
  }

  return (
    <div className="space-y-6">
      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        {cards.map((card) => <KpiCard key={card.title} {...card} />)}
      </div>

      <div className="grid gap-6 xl:grid-cols-[1.35fr_0.95fr]">
        <Card className="border-slate-200 shadow-sm">
          <CardHeader>
            <CardTitle>Daily message volume</CardTitle>
            <CardDescription>Real message traffic across channels in the selected window.</CardDescription>
          </CardHeader>
          <CardContent className="h-[320px]">
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={overview?.dailyVolume || []}>
                <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#e2e8f0" />
                <XAxis dataKey="day" tickLine={false} axisLine={false} />
                <YAxis tickLine={false} axisLine={false} />
                <Tooltip />
                <Legend />
                <Bar dataKey="whatsapp" name="WhatsApp" fill="#25D366" radius={[6, 6, 0, 0]} />
                <Bar dataKey="sms" name="SMS" fill="#f97316" radius={[6, 6, 0, 0]} />
                <Bar dataKey="email" name="Email" fill="#3b82f6" radius={[6, 6, 0, 0]} />
                <Bar dataKey="other" name="Other" fill="#64748b" radius={[6, 6, 0, 0]} />
              </BarChart>
            </ResponsiveContainer>
          </CardContent>
        </Card>

        <Card className="border-slate-200 shadow-sm">
          <CardHeader>
            <CardTitle>Channel distribution</CardTitle>
            <CardDescription>Message share by channel for this period.</CardDescription>
          </CardHeader>
          <CardContent className="h-[320px]">
            <ResponsiveContainer width="100%" height="100%">
              <PieChart>
                <Pie data={overview?.channelDistribution || []} dataKey="value" nameKey="name" innerRadius={65} outerRadius={95} paddingAngle={4}>
                  {(overview?.channelDistribution || []).map((entry, index) => <Cell key={`${entry.name}-${index}`} fill={COLORS[index % COLORS.length]} />)}
                </Pie>
                <Tooltip />
                <Legend />
              </PieChart>
            </ResponsiveContainer>
          </CardContent>
        </Card>
      </div>

      <div className="grid gap-6 xl:grid-cols-[1fr_1fr]">
        <Card className="border-slate-200 shadow-sm">
          <CardHeader>
            <CardTitle>Webhook status summary</CardTitle>
            <CardDescription>Status analytics from message events and webhook processing.</CardDescription>
          </CardHeader>
          <CardContent className="h-[300px]">
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={statusRows}>
                <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#e2e8f0" />
                <XAxis dataKey="name" tickLine={false} axisLine={false} />
                <YAxis tickLine={false} axisLine={false} />
                <Tooltip />
                <Bar dataKey="value" fill="#f97316" radius={[8, 8, 0, 0]} />
              </BarChart>
            </ResponsiveContainer>
          </CardContent>
        </Card>

        <Card className="border-slate-200 shadow-sm">
          <CardHeader>
            <CardTitle>Hourly traffic</CardTitle>
            <CardDescription>Message creation volume by hour of day.</CardDescription>
          </CardHeader>
          <CardContent className="h-[300px]">
            <ResponsiveContainer width="100%" height="100%">
              <LineChart data={overview?.hourlyVolume || []}>
                <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#e2e8f0" />
                <XAxis dataKey="hour" tickLine={false} axisLine={false} />
                <YAxis tickLine={false} axisLine={false} />
                <Tooltip />
                <Line type="monotone" dataKey="messages" stroke="#f97316" strokeWidth={3} dot={{ fill: "#f97316", r: 3 }} />
              </LineChart>
            </ResponsiveContainer>
          </CardContent>
        </Card>
      </div>

      <div className="grid gap-6 xl:grid-cols-[1.25fr_0.75fr]">
        <Card className="border-slate-200 shadow-sm">
          <CardHeader>
            <CardTitle>Campaign performance</CardTitle>
            <CardDescription>Recent campaigns with actual sent, delivered, read, and failed counts.</CardDescription>
          </CardHeader>
          <CardContent>
            <div className="rounded-2xl border border-slate-200 overflow-hidden">
              <table className="w-full text-sm">
                <thead className="bg-slate-50 text-slate-600">
                  <tr>
                    <th className="px-4 py-3 text-left font-semibold">Campaign</th>
                    <th className="px-4 py-3 text-left font-semibold">Channel</th>
                    <th className="px-4 py-3 text-right font-semibold">Sent</th>
                    <th className="px-4 py-3 text-right font-semibold">Delivered</th>
                    <th className="px-4 py-3 text-right font-semibold">Read</th>
                    <th className="px-4 py-3 text-right font-semibold">Failed</th>
                  </tr>
                </thead>
                <tbody>
                  {(overview?.campaignPerformance || []).map((row) => (
                    <tr key={row.id} className="border-t border-slate-100">
                      <td className="px-4 py-3 font-medium text-slate-950">{row.name}</td>
                      <td className="px-4 py-3 text-slate-600">{row.channel}</td>
                      <td className="px-4 py-3 text-right">{number(row.sent)}</td>
                      <td className="px-4 py-3 text-right">{number(row.delivered)}</td>
                      <td className="px-4 py-3 text-right">{number(row.read)}</td>
                      <td className="px-4 py-3 text-right text-rose-600">{number(row.failed)}</td>
                    </tr>
                  ))}
                  {!(overview?.campaignPerformance || []).length ? (
                    <tr>
                      <td colSpan={6} className="px-4 py-8 text-center text-slate-500">No campaign analytics available yet.</td>
                    </tr>
                  ) : null}
                </tbody>
              </table>
            </div>
          </CardContent>
        </Card>

        <Card className="border-slate-200 shadow-sm">
          <CardHeader>
            <CardTitle>Failure diagnostics</CardTitle>
            <CardDescription>Top failure codes from outbound event processing.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-3">
            {failureRows.map((row) => (
              <div key={row.name} className="flex items-center justify-between rounded-xl border border-slate-100 bg-slate-50 px-4 py-3 text-sm">
                <span className="font-medium text-slate-700">{row.name}</span>
                <Badge variant="outline">{row.value}</Badge>
              </div>
            ))}
            {!failureRows.length ? <p className="text-sm text-slate-500">No failures recorded in this period.</p> : null}
          </CardContent>
        </Card>
      </div>

      <div className="flex justify-end">
        <Button variant="outline" onClick={() => load(false)} disabled={refreshing}>
          <RefreshCcw className={`mr-2 h-4 w-4 ${refreshing ? "animate-spin" : ""}`} />
          Refresh analytics
        </Button>
      </div>
    </div>
  );
}

function PlatformAnalytics({ days }) {
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [customers, setCustomers] = useState([]);
  const [webhook, setWebhook] = useState(null);
  const [queueHealth, setQueueHealth] = useState(null);
  const [securitySignals, setSecuritySignals] = useState([]);
  const [mobileTelemetry, setMobileTelemetry] = useState([]);
  const [tenantId, setTenantId] = useState("all");

  const load = async (showSpinner = true) => {
    if (showSpinner) setLoading(true);
    else setRefreshing(true);
    try {
      const [customerRows, webhookRes, queueRes, securityRes, mobileRes] = await Promise.all([
        getPlatformCustomers("").catch(() => []),
        getPlatformWebhookAnalytics(tenantId === "all" ? "" : tenantId, days).catch(() => null),
        getPlatformQueueHealth().catch(() => null),
        getPlatformSecuritySignals({ status: "open", limit: 100 }).catch(() => []),
        getPlatformMobileTelemetry({ take: 250, days: Math.min(days, 30) }).catch(() => []),
      ]);
      setCustomers(Array.isArray(customerRows) ? customerRows : []);
      setWebhook(webhookRes || null);
      setQueueHealth(queueRes || null);
      setSecuritySignals(Array.isArray(securityRes) ? securityRes : []);
      setMobileTelemetry(Array.isArray(mobileRes) ? mobileRes : []);
    } catch (e) {
      toast.error(e?.message || "Failed to load platform analytics");
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  };

  useEffect(() => {
    load(true);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [days, tenantId]);

  const summary = useMemo(() => {
    const activeStatuses = new Set(["active", "trialing", "trial"]);
    return {
      totalTenants: customers.length,
      activeTenants: customers.filter((row) => activeStatuses.has(String(row.subscriptionStatus || "").toLowerCase())).length,
      totalUsers: customers.reduce((acc, row) => acc + Number(row.users || 0), 0),
      totalRevenue: customers.reduce((acc, row) => acc + Number(row.totalRevenue || 0), 0),
      mrr: customers
        .filter((row) => activeStatuses.has(String(row.subscriptionStatus || "").toLowerCase()))
        .reduce((acc, row) => acc + Number(row.monthlyPrice || 0), 0),
      openSignals: securitySignals.filter((row) => String(row.status || "").toLowerCase() !== "resolved").length,
      telemetry: mobileTelemetry.length,
    };
  }, [customers, securitySignals, mobileTelemetry]);

  const webhookRows = useMemo(() => {
    return Object.entries(webhook?.statusSummary || {}).map(([name, value]) => ({ name, value: Number(value || 0) }));
  }, [webhook]);

  const planMix = useMemo(() => {
    const map = new Map();
    customers.forEach((row) => map.set(row.planName || "No Plan", (map.get(row.planName || "No Plan") || 0) + 1));
    return [...map.entries()].map(([name, value]) => ({ name, value })).sort((a, b) => b.value - a.value).slice(0, 8);
  }, [customers]);

  const topRevenue = useMemo(() => {
    return [...customers]
      .sort((a, b) => Number(b.totalRevenue || 0) - Number(a.totalRevenue || 0))
      .slice(0, 8)
      .map((row) => ({ name: row.companyName || row.tenantName || row.tenantSlug, revenue: Number(row.totalRevenue || 0) }));
  }, [customers]);

  const securityBreakdown = useMemo(() => {
    const map = new Map();
    securitySignals.forEach((row) => {
      const key = String(row.signalType || row.type || "unknown");
      map.set(key, (map.get(key) || 0) + 1);
    });
    return [...map.entries()].map(([name, value]) => ({ name, value })).sort((a, b) => b.value - a.value).slice(0, 8);
  }, [securitySignals]);

  if (loading) {
    return <div className="rounded-3xl border border-slate-200 bg-white p-10 text-sm text-slate-500">Loading platform analytics...</div>;
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-3 md:flex-row md:items-center md:justify-between">
        <div className="flex items-center gap-3">
          <Select value={tenantId} onValueChange={setTenantId}>
            <SelectTrigger className="w-[260px]">
              <SelectValue placeholder="All tenants" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">All tenants</SelectItem>
              {customers.map((row) => (
                <SelectItem key={row.tenantId} value={row.tenantId}>{row.companyName || row.tenantName || row.tenantSlug}</SelectItem>
              ))}
            </SelectContent>
          </Select>
          <Badge className="bg-slate-100 text-slate-700 hover:bg-slate-100">Owner analytics</Badge>
        </div>
        <Button variant="outline" onClick={() => load(false)} disabled={refreshing}>
          <RefreshCcw className={`mr-2 h-4 w-4 ${refreshing ? "animate-spin" : ""}`} />
          Refresh analytics
        </Button>
      </div>

      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        <KpiCard title="Tenants" value={number(summary.totalTenants)} hint={`${number(summary.activeTenants)} active`} icon={Building2} tone="orange" />
        <KpiCard title="MRR" value={money(summary.mrr)} hint="Active recurring value" icon={CreditCard} tone="green" />
        <KpiCard title="Platform Users" value={number(summary.totalUsers)} hint={`${number(summary.telemetry)} recent device events`} icon={Users} tone="blue" />
        <KpiCard title="Open Signals" value={number(summary.openSignals)} hint={`${money(summary.totalRevenue)} lifetime invoice revenue`} icon={ShieldAlert} tone="rose" />
      </div>

      <div className="grid gap-6 xl:grid-cols-[1.25fr_0.75fr]">
        <Card className="border-slate-200 shadow-sm">
          <CardHeader>
            <CardTitle>Webhook outcome analytics</CardTitle>
            <CardDescription>{tenantId === "all" ? "Platform-wide control plane and webhook outcomes." : "Selected tenant webhook and outbound event outcomes."}</CardDescription>
          </CardHeader>
          <CardContent className="h-[320px]">
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={webhookRows}>
                <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#e2e8f0" />
                <XAxis dataKey="name" tickLine={false} axisLine={false} />
                <YAxis tickLine={false} axisLine={false} />
                <Tooltip />
                <Bar dataKey="value" fill="#f97316" radius={[8, 8, 0, 0]} />
              </BarChart>
            </ResponsiveContainer>
          </CardContent>
        </Card>

        <Card className="border-slate-200 shadow-sm">
          <CardHeader>
            <CardTitle>Queue health</CardTitle>
            <CardDescription>Current outbound and webhook processing pressure.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-3 text-sm">
            <div className="rounded-xl border border-slate-100 bg-slate-50 px-4 py-3 flex items-center justify-between">
              <span className="text-slate-700">Outbound provider</span>
              <span className="font-semibold text-slate-950">{queueHealth?.outbound?.provider || "unknown"}</span>
            </div>
            <div className="rounded-xl border border-slate-100 bg-slate-50 px-4 py-3 flex items-center justify-between">
              <span className="text-slate-700">Outbound depth</span>
              <span className="font-semibold text-slate-950">{number(queueHealth?.outbound?.depth || 0)}</span>
            </div>
            <div className="rounded-xl border border-slate-100 bg-slate-50 px-4 py-3 flex items-center justify-between">
              <span className="text-slate-700">Webhook depth</span>
              <span className="font-semibold text-slate-950">{number(queueHealth?.webhook?.depth || 0)}</span>
            </div>
            <div className="rounded-xl border border-slate-100 bg-slate-50 px-4 py-3 flex items-center justify-between">
              <span className="text-slate-700">Dead letters (24h)</span>
              <span className="font-semibold text-rose-600">{number(queueHealth?.webhook?.deadLetter24h || 0)}</span>
            </div>
          </CardContent>
        </Card>
      </div>

      <div className="grid gap-6 xl:grid-cols-[1fr_1fr]">
        <Card className="border-slate-200 shadow-sm">
          <CardHeader>
            <CardTitle>Plan mix</CardTitle>
            <CardDescription>Current tenant distribution by commercial plan.</CardDescription>
          </CardHeader>
          <CardContent className="h-[300px]">
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={planMix} layout="vertical" margin={{ left: 40 }}>
                <CartesianGrid strokeDasharray="3 3" horizontal={false} stroke="#e2e8f0" />
                <XAxis type="number" tickLine={false} axisLine={false} />
                <YAxis dataKey="name" type="category" tickLine={false} axisLine={false} width={120} />
                <Tooltip />
                <Bar dataKey="value" fill="#3b82f6" radius={[0, 8, 8, 0]} />
              </BarChart>
            </ResponsiveContainer>
          </CardContent>
        </Card>

        <Card className="border-slate-200 shadow-sm">
          <CardHeader>
            <CardTitle>Top revenue accounts</CardTitle>
            <CardDescription>Highest invoice revenue recorded per customer.</CardDescription>
          </CardHeader>
          <CardContent className="h-[300px]">
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={topRevenue}>
                <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#e2e8f0" />
                <XAxis dataKey="name" tickLine={false} axisLine={false} interval={0} angle={-10} textAnchor="end" height={70} />
                <YAxis tickLine={false} axisLine={false} />
                <Tooltip formatter={(value) => money(value)} />
                <Bar dataKey="revenue" fill="#22c55e" radius={[8, 8, 0, 0]} />
              </BarChart>
            </ResponsiveContainer>
          </CardContent>
        </Card>
      </div>

      <div className="grid gap-6 xl:grid-cols-[0.9fr_1.1fr]">
        <Card className="border-slate-200 shadow-sm">
          <CardHeader>
            <CardTitle>Security signal mix</CardTitle>
            <CardDescription>Open signal types currently visible to the platform owner.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-3">
            {securityBreakdown.map((row) => (
              <div key={row.name} className="flex items-center justify-between rounded-xl border border-slate-100 bg-slate-50 px-4 py-3 text-sm">
                <span className="font-medium text-slate-700">{row.name}</span>
                <Badge variant="outline">{row.value}</Badge>
              </div>
            ))}
            {!securityBreakdown.length ? <p className="text-sm text-slate-500">No open platform security signals.</p> : null}
          </CardContent>
        </Card>

        <Card className="border-slate-200 shadow-sm">
          <CardHeader>
            <CardTitle>Platform reporting snapshot</CardTitle>
            <CardDescription>Owner-level operational counts derived from live control data.</CardDescription>
          </CardHeader>
          <CardContent className="grid gap-4 sm:grid-cols-2">
            <div className="rounded-2xl border border-slate-100 bg-slate-50 p-4">
              <p className="text-xs uppercase tracking-[0.16em] text-slate-500">Lifetime invoice revenue</p>
              <p className="mt-2 text-3xl font-bold text-slate-950">{money(summary.totalRevenue)}</p>
            </div>
            <div className="rounded-2xl border border-slate-100 bg-slate-50 p-4">
              <p className="text-xs uppercase tracking-[0.16em] text-slate-500">Recent device telemetry</p>
              <p className="mt-2 text-3xl font-bold text-slate-950">{number(summary.telemetry)}</p>
              <p className="mt-1 text-sm text-slate-500">Last {Math.min(days, 30)} days</p>
            </div>
            <div className="rounded-2xl border border-slate-100 bg-slate-50 p-4">
              <p className="text-xs uppercase tracking-[0.16em] text-slate-500">Selected tenant scope</p>
              <p className="mt-2 text-xl font-bold text-slate-950">{tenantId === "all" ? "All tenants" : (customers.find((x) => x.tenantId === tenantId)?.companyName || customers.find((x) => x.tenantId === tenantId)?.tenantSlug || "Selected tenant")}</p>
            </div>
            <div className="rounded-2xl border border-slate-100 bg-slate-50 p-4">
              <p className="text-xs uppercase tracking-[0.16em] text-slate-500">Owner signals</p>
              <p className="mt-2 text-3xl font-bold text-slate-950">{number(summary.openSignals)}</p>
              <p className="mt-1 text-sm text-slate-500">Requires review or policy action</p>
            </div>
          </CardContent>
        </Card>
      </div>
    </div>
  );
}

const AnalyticsPage = () => {
  const session = getSession();
  const isPlatformOwner = (session.role || "").toLowerCase() === "super_admin";
  const ownerMode = (() => {
    if (!isPlatformOwner) return "self";
    try {
      return localStorage.getItem("textzy_owner_mode") || "self";
    } catch {
      return "self";
    }
  })();
  const isPlatformView = isPlatformOwner && ownerMode === "platform";
  const [days, setDays] = useState("30");

  return (
    <div className="space-y-6" data-testid="analytics-page">
      <div className="flex flex-col gap-4 md:flex-row md:items-center md:justify-between">
        <div>
          <h1 className="text-2xl font-heading font-bold text-slate-900">Analytics</h1>
          <p className="text-slate-600">
            {isPlatformView
              ? "Platform-wide operational and commercial reporting"
              : "Live tenant messaging analytics from campaigns, messages, and webhook outcomes"}
          </p>
        </div>
        <div className="flex items-center gap-3">
          <Select value={days} onValueChange={setDays}>
            <SelectTrigger className="w-44" data-testid="date-range-select">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="7">Last 7 days</SelectItem>
              <SelectItem value="30">Last 30 days</SelectItem>
              <SelectItem value="90">Last 90 days</SelectItem>
            </SelectContent>
          </Select>
          <Badge className="bg-orange-50 text-orange-600 hover:bg-orange-50">
            <BarChart3 className="mr-1 h-3.5 w-3.5" />
            {isPlatformView ? "Platform" : "Tenant"}
          </Badge>
        </div>
      </div>

      {isPlatformView ? <PlatformAnalytics days={Number(days)} /> : <TenantAnalytics days={Number(days)} />}
    </div>
  );
};

export default AnalyticsPage;
