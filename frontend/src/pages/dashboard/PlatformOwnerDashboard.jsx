import { useEffect, useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Progress } from "@/components/ui/progress";
import { Input } from "@/components/ui/input";
import {
  getPlatformCustomers,
  getPlatformQueueHealth,
  getPlatformSecuritySignals,
  getPlatformWebhookAnalytics,
  getPlatformWabaOnboardingSummary,
  getPlatformMobileTelemetry,
  listPlatformBillingPlans,
  getSession,
} from "@/lib/api";
import { toast } from "sonner";
import {
  Activity,
  AlertTriangle,
  ArrowRight,
  Building2,
  CreditCard,
  Database,
  Layers3,
  RefreshCcw,
  Rocket,
  ServerCog,
  ShieldAlert,
  Smartphone,
  Users,
  Wifi,
} from "lucide-react";
import { BarChart, Bar, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";

const money = (value) => `INR ${Number(value || 0).toLocaleString()}`;
const number = (value) => Number(value || 0).toLocaleString();

function MiniKpi({ title, value, hint, icon: Icon, tone = "orange" }) {
  const toneClasses = {
    orange: "bg-orange-50 text-orange-600 border-orange-100",
    emerald: "bg-emerald-50 text-emerald-600 border-emerald-100",
    blue: "bg-blue-50 text-blue-600 border-blue-100",
    violet: "bg-violet-50 text-violet-600 border-violet-100",
    rose: "bg-rose-50 text-rose-600 border-rose-100",
    slate: "bg-slate-50 text-slate-600 border-slate-100",
  };
  return (
    <Card className="border-slate-200 shadow-sm">
      <CardContent className="pt-5">
        <div className="flex items-start justify-between gap-4">
          <div>
            <p className="text-xs font-semibold uppercase tracking-[0.16em] text-slate-500">{title}</p>
            <p className="mt-2 text-3xl font-bold text-slate-950">{value}</p>
            <p className="mt-1 text-sm text-slate-500">{hint}</p>
          </div>
          <div className={`inline-flex h-11 w-11 items-center justify-center rounded-2xl border ${toneClasses[tone] || toneClasses.orange}`}>
            <Icon className="h-5 w-5" />
          </div>
        </div>
      </CardContent>
    </Card>
  );
}

function StatusRow({ label, value, helper, valueClassName = "text-slate-950" }) {
  return (
    <div className="flex items-center justify-between gap-4 rounded-xl border border-slate-100 bg-slate-50/70 px-4 py-3">
      <div>
        <p className="text-sm font-medium text-slate-700">{label}</p>
        {helper ? <p className="text-xs text-slate-500">{helper}</p> : null}
      </div>
      <p className={`text-sm font-semibold ${valueClassName}`}>{value}</p>
    </div>
  );
}

export default function PlatformOwnerDashboard() {
  const navigate = useNavigate();
  const session = getSession();
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [search, setSearch] = useState("");
  const [customers, setCustomers] = useState([]);
  const [plans, setPlans] = useState([]);
  const [queueHealth, setQueueHealth] = useState(null);
  const [securitySignals, setSecuritySignals] = useState([]);
  const [webhookAnalytics, setWebhookAnalytics] = useState(null);
  const [onboardingSummary, setOnboardingSummary] = useState(null);
  const [mobileTelemetry, setMobileTelemetry] = useState([]);

  const load = async (query = "") => {
    const isFirstLoad = !customers.length && !refreshing;
    if (isFirstLoad) setLoading(true);
    else setRefreshing(true);
    try {
      const [
        customerRows,
        billingPlans,
        queue,
        security,
        webhook,
        onboarding,
        mobile,
      ] = await Promise.all([
        getPlatformCustomers(query).catch(() => []),
        listPlatformBillingPlans().catch(() => []),
        getPlatformQueueHealth().catch(() => null),
        getPlatformSecuritySignals({ status: "open", limit: 50 }).catch(() => []),
        getPlatformWebhookAnalytics("", 7).catch(() => null),
        getPlatformWabaOnboardingSummary().catch(() => null),
        getPlatformMobileTelemetry({ take: 200, days: 1 }).catch(() => []),
      ]);

      setCustomers(Array.isArray(customerRows) ? customerRows : []);
      setPlans(Array.isArray(billingPlans) ? billingPlans : []);
      setQueueHealth(queue || null);
      setSecuritySignals(Array.isArray(security) ? security : []);
      setWebhookAnalytics(webhook || null);
      setOnboardingSummary(onboarding || null);
      setMobileTelemetry(Array.isArray(mobile) ? mobile : []);
    } catch (e) {
      toast.error(e?.message || "Failed to load platform dashboard");
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  };

  useEffect(() => {
    load("");
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const filteredCustomers = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return customers;
    return customers.filter((row) =>
      [row.tenantName, row.tenantSlug, row.companyName, row.ownerEmail, row.ownerName]
        .filter(Boolean)
        .some((value) => String(value).toLowerCase().includes(q)),
    );
  }, [customers, search]);

  const summary = useMemo(() => {
    const activeStatuses = new Set(["active", "trialing", "trial"]);
    const totalTenants = customers.length;
    const activeTenants = customers.filter((row) => activeStatuses.has(String(row.subscriptionStatus || "").toLowerCase())).length;
    const noPlanTenants = customers.filter((row) => !String(row.planCode || "").trim()).length;
    const totalUsers = customers.reduce((acc, row) => acc + Number(row.users || 0), 0);
    const activeUsers = customers.reduce((acc, row) => acc + Number(row.activeUsers || 0), 0);
    const totalRevenue = customers.reduce((acc, row) => acc + Number(row.totalRevenue || 0), 0);
    const monthlyRecurring = customers
      .filter((row) => activeStatuses.has(String(row.subscriptionStatus || "").toLowerCase()))
      .reduce((acc, row) => acc + Number(row.monthlyPrice || 0), 0);
    const activePlans = plans.filter((plan) => plan?.isActive).length;
    const openSignals = securitySignals.filter((signal) => String(signal.status || "").toLowerCase() !== "resolved").length;
    const mobileDevices = mobileTelemetry.length;
    return {
      totalTenants,
      activeTenants,
      noPlanTenants,
      totalUsers,
      activeUsers,
      totalRevenue,
      monthlyRecurring,
      activePlans,
      openSignals,
      mobileDevices,
    };
  }, [customers, plans, securitySignals, mobileTelemetry]);

  const onboardingCounts = useMemo(() => onboardingSummary?.counts || {}, [onboardingSummary]);
  const onboardingRows = useMemo(() => {
    const entries = [
      ["ready", "Ready"],
      ["webhook_subscribed", "Webhook"],
      ["assets_linked", "Assets Linked"],
      ["requested", "Requested"],
      ["degraded", "Degraded"],
      ["error", "Errors"],
    ];
    return entries.map(([key, label]) => ({
      key,
      label,
      count: Number(onboardingCounts[key] || 0),
    }));
  }, [onboardingCounts]);

  const webhookRows = useMemo(() => {
    const map = webhookAnalytics?.statusSummary || {};
    return [
      { name: "Accepted", value: Number(map.AcceptedByMeta || map.Accepted || 0) },
      { name: "Sent", value: Number(map.Sent || 0) },
      { name: "Delivered", value: Number(map.Delivered || 0) },
      { name: "Read", value: Number(map.Read || 0) },
      { name: "Failed", value: Number(map.Failed || 0) },
    ];
  }, [webhookAnalytics]);

  const planMix = useMemo(() => {
    const map = new Map();
    for (const row of customers) {
      const key = row.planName || "No Plan";
      map.set(key, (map.get(key) || 0) + 1);
    }
    return [...map.entries()]
      .map(([name, count]) => ({ name, count }))
      .sort((a, b) => b.count - a.count)
      .slice(0, 6);
  }, [customers]);

  const topCustomers = useMemo(() => {
    return [...filteredCustomers]
      .sort((a, b) => Number(b.totalRevenue || 0) - Number(a.totalRevenue || 0))
      .slice(0, 8);
  }, [filteredCustomers]);

  const securityBreakdown = useMemo(() => {
    const map = new Map();
    for (const signal of securitySignals) {
      const type = String(signal.signalType || signal.type || "unknown");
      map.set(type, (map.get(type) || 0) + 1);
    }
    return [...map.entries()].map(([type, count]) => ({ type, count })).slice(0, 6);
  }, [securitySignals]);

  const outboundDepth = Number(queueHealth?.outbound?.depth || 0);
  const webhookDepth = Number(queueHealth?.webhook?.depth || 0);
  const queueStress = Math.min(100, outboundDepth + webhookDepth > 0 ? (webhookDepth + outboundDepth) * 5 : 0);

  return (
    <div className="space-y-6" data-testid="platform-dashboard">
      <section className="rounded-[28px] border border-slate-200 bg-gradient-to-br from-slate-950 via-slate-900 to-orange-950 p-6 text-white shadow-sm">
        <div className="flex flex-col gap-6 xl:flex-row xl:items-end xl:justify-between">
          <div className="max-w-3xl">
            <Badge className="border border-white/15 bg-white/10 text-white hover:bg-white/10">Platform Control</Badge>
            <h1 className="mt-4 text-3xl font-bold tracking-tight">SaaS command center for {session.email || "platform owner"}</h1>
            <p className="mt-3 max-w-2xl text-sm text-slate-200">
              Monitor customer growth, queue health, WABA onboarding, security posture, and recurring revenue from one owner console.
            </p>
            <div className="mt-5 flex flex-wrap gap-3">
              <Button className="bg-orange-500 text-white hover:bg-orange-600" onClick={() => navigate("/dashboard/admin")}>
                Open customer admin
              </Button>
              <Button variant="outline" className="border-white/20 bg-white/5 text-white hover:bg-white/10" onClick={() => navigate("/dashboard/platform-settings?tab=billing-plans")}>
                Billing plans
              </Button>
              <Button variant="outline" className="border-white/20 bg-white/5 text-white hover:bg-white/10" onClick={() => navigate("/dashboard/platform-settings?tab=security-ops")}>
                Security ops
              </Button>
            </div>
          </div>
          <div className="grid min-w-full grid-cols-2 gap-3 xl:min-w-[420px]">
            <div className="rounded-2xl border border-white/10 bg-white/5 p-4">
              <p className="text-xs uppercase tracking-[0.16em] text-slate-300">MRR</p>
              <p className="mt-2 text-2xl font-bold">{money(summary.monthlyRecurring)}</p>
              <p className="mt-1 text-xs text-slate-300">Active recurring value across live subscriptions</p>
            </div>
            <div className="rounded-2xl border border-white/10 bg-white/5 p-4">
              <p className="text-xs uppercase tracking-[0.16em] text-slate-300">Tenants</p>
              <p className="mt-2 text-2xl font-bold">{number(summary.totalTenants)}</p>
              <p className="mt-1 text-xs text-slate-300">{number(summary.activeTenants)} active, {number(summary.noPlanTenants)} without plan</p>
            </div>
          </div>
        </div>
      </section>

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-5">
        <MiniKpi title="Customers" value={number(summary.totalTenants)} hint={`${number(summary.activeTenants)} active tenants`} icon={Building2} tone="orange" />
        <MiniKpi title="Platform Users" value={number(summary.totalUsers)} hint={`${number(summary.activeUsers)} active users`} icon={Users} tone="blue" />
        <MiniKpi title="Active Plans" value={number(summary.activePlans)} hint={money(summary.totalRevenue)} icon={CreditCard} tone="emerald" />
        <MiniKpi title="Open Signals" value={number(summary.openSignals)} hint="Security items awaiting review" icon={ShieldAlert} tone="rose" />
        <MiniKpi title="Mobile Devices" value={number(summary.mobileDevices)} hint="Recent device telemetry seen" icon={Smartphone} tone="violet" />
      </div>

      <div className="grid gap-6 2xl:grid-cols-[1.35fr_0.95fr]">
        <Card className="border-slate-200 shadow-sm">
          <CardHeader className="flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
            <div>
              <CardTitle>Customer portfolio</CardTitle>
              <CardDescription>Search customers, inspect commercial health, and jump into admin actions.</CardDescription>
            </div>
            <div className="flex w-full gap-3 sm:w-auto">
              <Input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Search company, tenant, owner" className="sm:w-72" />
              <Button variant="outline" onClick={() => load(search)} disabled={refreshing}>
                <RefreshCcw className={`mr-2 h-4 w-4 ${refreshing ? "animate-spin" : ""}`} />
                Refresh
              </Button>
            </div>
          </CardHeader>
          <CardContent>
            <div className="rounded-2xl border border-slate-200 overflow-hidden">
              <table className="w-full text-sm">
                <thead className="bg-slate-50 text-slate-600">
                  <tr>
                    <th className="px-4 py-3 text-left font-semibold">Company</th>
                    <th className="px-4 py-3 text-left font-semibold">Owner</th>
                    <th className="px-4 py-3 text-left font-semibold">Plan</th>
                    <th className="px-4 py-3 text-left font-semibold">Users</th>
                    <th className="px-4 py-3 text-left font-semibold">Revenue</th>
                    <th className="px-4 py-3 text-left font-semibold">Status</th>
                    <th className="px-4 py-3 text-right font-semibold">Action</th>
                  </tr>
                </thead>
                <tbody>
                  {topCustomers.map((row) => {
                    const isActive = ["active", "trial", "trialing"].includes(String(row.subscriptionStatus || "").toLowerCase());
                    return (
                      <tr key={row.tenantId} className="border-t border-slate-100">
                        <td className="px-4 py-3">
                          <div>
                            <p className="font-semibold text-slate-950">{row.companyName || row.tenantName}</p>
                            <p className="text-xs text-slate-500">{row.tenantSlug}</p>
                          </div>
                        </td>
                        <td className="px-4 py-3">
                          <div>
                            <p className="text-slate-900">{row.ownerName || "-"}</p>
                            <p className="text-xs text-slate-500">{row.ownerEmail || "-"}</p>
                          </div>
                        </td>
                        <td className="px-4 py-3">
                          <div>
                            <p className="font-medium text-slate-900">{row.planName || "No Plan"}</p>
                            <p className="text-xs text-slate-500">{money(row.monthlyPrice || 0)}/month</p>
                          </div>
                        </td>
                        <td className="px-4 py-3">{number(row.users)}</td>
                        <td className="px-4 py-3">{money(row.totalRevenue)}</td>
                        <td className="px-4 py-3">
                          <Badge className={isActive ? "bg-emerald-100 text-emerald-700 hover:bg-emerald-100" : "bg-amber-100 text-amber-700 hover:bg-amber-100"}>
                            {row.subscriptionStatus || "none"}
                          </Badge>
                        </td>
                        <td className="px-4 py-3 text-right">
                          <Button variant="ghost" size="sm" onClick={() => navigate("/dashboard/admin")}>
                            Open
                            <ArrowRight className="ml-2 h-4 w-4" />
                          </Button>
                        </td>
                      </tr>
                    );
                  })}
                  {!topCustomers.length ? (
                    <tr>
                      <td colSpan={7} className="px-4 py-10 text-center text-sm text-slate-500">
                        {loading ? "Loading platform customers..." : "No platform customers found."}
                      </td>
                    </tr>
                  ) : null}
                </tbody>
              </table>
            </div>
          </CardContent>
        </Card>

        <div className="space-y-6">
          <Card className="border-slate-200 shadow-sm">
            <CardHeader>
              <CardTitle>Queue and delivery health</CardTitle>
              <CardDescription>Outbound queue provider, webhook backlog, and delivery processing pressure.</CardDescription>
            </CardHeader>
            <CardContent className="space-y-4">
              <StatusRow label="Outbound Queue" value={`${queueHealth?.outbound?.provider || "unknown"} / depth ${number(outboundDepth)}`} helper="Primary outbound dispatch provider" />
              <StatusRow label="Webhook Queue" value={`${queueHealth?.webhook?.provider || "unknown"} / depth ${number(webhookDepth)}`} helper={`Processing ${number(queueHealth?.webhook?.processing || 0)} • Retry ${number(queueHealth?.webhook?.retrying || 0)}`} />
              <StatusRow label="Dead Letters (24h)" value={number(queueHealth?.webhook?.deadLetter24h || 0)} helper={`Unmapped ${number(queueHealth?.webhook?.unmapped24h || 0)} in last 24 hours`} valueClassName="text-rose-600" />
              <div className="space-y-2">
                <div className="flex items-center justify-between text-sm">
                  <span className="font-medium text-slate-700">Queue stress</span>
                  <span className="text-slate-500">{queueStress}%</span>
                </div>
                <Progress value={queueStress} className="h-2" />
              </div>
            </CardContent>
          </Card>

          <Card className="border-slate-200 shadow-sm">
            <CardHeader>
              <CardTitle>WABA rollout state</CardTitle>
              <CardDescription>Tenant onboarding readiness from Meta asset mapping to ready state.</CardDescription>
            </CardHeader>
            <CardContent className="grid gap-3 sm:grid-cols-2">
              {onboardingRows.map((row) => (
                <div key={row.key} className="rounded-2xl border border-slate-100 bg-slate-50 p-4">
                  <p className="text-xs uppercase tracking-[0.16em] text-slate-500">{row.label}</p>
                  <p className="mt-2 text-2xl font-bold text-slate-950">{number(row.count)}</p>
                </div>
              ))}
              <Button variant="outline" className="sm:col-span-2" onClick={() => navigate("/dashboard/platform-settings?tab=waba-onboarding")}>
                Open onboarding control
              </Button>
            </CardContent>
          </Card>
        </div>
      </div>

      <div className="grid gap-6 xl:grid-cols-[1.05fr_0.95fr_0.9fr]">
        <Card className="border-slate-200 shadow-sm">
          <CardHeader>
            <CardTitle>Webhook outcome trend</CardTitle>
            <CardDescription>Global webhook/control-plane status counts for the last 7 days.</CardDescription>
          </CardHeader>
          <CardContent className="h-[300px]">
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
            <CardTitle>Plan mix</CardTitle>
            <CardDescription>Where your customer base currently sits across commercial plans.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            {planMix.map((row) => {
              const percent = summary.totalTenants > 0 ? Math.round((row.count / summary.totalTenants) * 100) : 0;
              return (
                <div key={row.name} className="space-y-2">
                  <div className="flex items-center justify-between text-sm">
                    <span className="font-medium text-slate-700">{row.name}</span>
                    <span className="text-slate-500">{row.count} tenants</span>
                  </div>
                  <Progress value={percent} className="h-2" />
                </div>
              );
            })}
            {!planMix.length ? <p className="text-sm text-slate-500">No plan distribution available yet.</p> : null}
          </CardContent>
        </Card>

        <Card className="border-slate-200 shadow-sm">
          <CardHeader>
            <CardTitle>Security and device watch</CardTitle>
            <CardDescription>Open platform risk items and device telemetry observed in the last day.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <StatusRow label="Open Security Signals" value={number(summary.openSignals)} helper="Requires owner review or automated mitigation" valueClassName="text-rose-600" />
            <StatusRow label="Telemetry Events" value={number(summary.mobileDevices)} helper="Recent mobile/device activity captured" valueClassName="text-blue-600" />
            <div className="space-y-2">
              {securityBreakdown.slice(0, 4).map((row) => (
                <div key={row.type} className="flex items-center justify-between text-sm text-slate-700">
                  <span>{row.type}</span>
                  <Badge variant="outline">{row.count}</Badge>
                </div>
              ))}
              {!securityBreakdown.length ? <p className="text-sm text-slate-500">No open security signals.</p> : null}
            </div>
            <Button variant="outline" className="w-full" onClick={() => navigate("/dashboard/platform-settings?tab=security-ops")}>
              Open security operations
            </Button>
          </CardContent>
        </Card>
      </div>

      <div className="grid gap-6 xl:grid-cols-[1fr_1fr]">
        <Card className="border-slate-200 shadow-sm">
          <CardHeader>
            <CardTitle>Owner actions</CardTitle>
            <CardDescription>Frequent control paths for platform billing, onboarding, logs, and settings.</CardDescription>
          </CardHeader>
          <CardContent className="grid gap-3 sm:grid-cols-2">
            {[
              { label: "Customer admin", hint: "Plans, invoices, tenant users", icon: Users, href: "/dashboard/admin" },
              { label: "WABA master", hint: "Meta app, tokens, onboarding", icon: Rocket, href: "/dashboard/platform-settings?tab=waba-master" },
              { label: "SMTP and email", hint: "Delivery, sender, reports", icon: Wifi, href: "/dashboard/platform-settings?tab=smtp-settings" },
              { label: "SMS gateway", hint: "Tata config and request logs", icon: ServerCog, href: "/dashboard/platform-settings?tab=sms-gateway" },
              { label: "Integration catalog", hint: "Paid/free add-ons and security catalog", icon: Layers3, href: "/dashboard/platform-settings?tab=integration-catalog" },
              { label: "Billing plans", hint: "Commercial packaging and limits", icon: CreditCard, href: "/dashboard/platform-settings?tab=billing-plans" },
              { label: "Request logs", hint: "Audit, webhook, idempotency", icon: Database, href: "/dashboard/platform-settings?tab=request-logs" },
            ].map((item) => (
              <button
                key={item.label}
                type="button"
                onClick={() => navigate(item.href)}
                className="rounded-2xl border border-slate-200 bg-white p-4 text-left transition hover:border-orange-200 hover:bg-orange-50/50"
              >
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <p className="font-semibold text-slate-950">{item.label}</p>
                    <p className="mt-1 text-sm text-slate-500">{item.hint}</p>
                  </div>
                  <item.icon className="h-5 w-5 text-orange-500" />
                </div>
              </button>
            ))}
          </CardContent>
        </Card>

        <Card className="border-slate-200 shadow-sm">
          <CardHeader>
            <CardTitle>Commercial posture</CardTitle>
            <CardDescription>How the platform is converting tenants into paid recurring accounts.</CardDescription>
          </CardHeader>
          <CardContent className="grid gap-4 sm:grid-cols-2">
            <div className="rounded-2xl border border-slate-100 bg-slate-50 p-4">
              <p className="text-xs uppercase tracking-[0.16em] text-slate-500">Active customer rate</p>
              <p className="mt-2 text-3xl font-bold text-slate-950">
                {summary.totalTenants > 0 ? Math.round((summary.activeTenants / summary.totalTenants) * 100) : 0}%
              </p>
              <p className="mt-1 text-sm text-slate-500">Tenants with active or trial subscriptions</p>
            </div>
            <div className="rounded-2xl border border-slate-100 bg-slate-50 p-4">
              <p className="text-xs uppercase tracking-[0.16em] text-slate-500">No plan accounts</p>
              <p className="mt-2 text-3xl font-bold text-slate-950">{number(summary.noPlanTenants)}</p>
              <p className="mt-1 text-sm text-slate-500">Candidates for onboarding or conversion</p>
            </div>
            <div className="rounded-2xl border border-slate-100 bg-slate-50 p-4 sm:col-span-2">
              <div className="flex items-center justify-between">
                <div>
                  <p className="text-xs uppercase tracking-[0.16em] text-slate-500">Lifetime invoice revenue</p>
                  <p className="mt-2 text-3xl font-bold text-slate-950">{money(summary.totalRevenue)}</p>
                </div>
                <Activity className="h-7 w-7 text-orange-500" />
              </div>
              <p className="mt-2 text-sm text-slate-500">Aggregated from recorded billing invoices across all tenants.</p>
            </div>
          </CardContent>
        </Card>
      </div>
    </div>
  );
}
