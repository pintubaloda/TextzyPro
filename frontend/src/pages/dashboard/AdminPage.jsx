import { useCallback, useEffect, useMemo, useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Switch } from "@/components/ui/switch";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import ApiDocsViewer from "@/components/docs/ApiDocsViewer";
import {
  ArrowUpRight,
  BadgeIndianRupee,
  BookOpenText,
  Building2,
  CreditCard,
  ExternalLink,
  Filter,
  FileText,
  KeyRound,
  Layers3,
  RefreshCcw,
  ShieldCheck,
  Users,
  UsersRound,
} from "lucide-react";
import { toast } from "sonner";
import {
  assignPlatformCustomerPlan,
  getPlatformCustomerActivity,
  getPlatformCustomerCompanySettings,
  getPlatformCustomerDetails,
  getPlatformCustomerFeatures,
  getPlatformCustomerInvoices,
  getPlatformCustomerMembers,
  getPlatformCustomerSubscriptions,
  getPlatformCustomerUsage,
  getPlatformCustomers,
  getPlatformSecurityReport,
  getPlatformUserTenants,
  getPlatformUsers,
  listPlatformBillingPlans,
  savePlatformCustomerCompanySettings,
  savePlatformCustomerFeatures,
} from "@/lib/api";

const DEFAULT_FEATURES = {
  smsGatewayReportEnabled: false,
};

const formatMoney = (value) =>
  `INR ${Number(value || 0).toLocaleString("en-IN", {
    maximumFractionDigits: 0,
  })}`;

const formatDate = (value) => {
  if (!value) return "-";
  return new Date(value).toLocaleDateString();
};

const formatDateTime = (value) => {
  if (!value) return "-";
  return new Date(value).toLocaleString();
};

const formatStatus = (value) => String(value || "unknown").replace(/[_-]/g, " ");

function KpiCard({ title, value, hint, icon: Icon, tone = "orange" }) {
  const tones = {
    orange: "border-orange-100 bg-orange-50 text-orange-600",
    emerald: "border-emerald-100 bg-emerald-50 text-emerald-600",
    blue: "border-blue-100 bg-blue-50 text-blue-600",
    violet: "border-violet-100 bg-violet-50 text-violet-600",
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
          <div className={`inline-flex h-11 w-11 items-center justify-center rounded-2xl border ${tones[tone] || tones.orange}`}>
            <Icon className="h-5 w-5" />
          </div>
        </div>
      </CardContent>
    </Card>
  );
}

function SectionTable({ headers, rows, empty }) {
  return (
    <div className="overflow-hidden rounded-2xl border border-slate-200 bg-white">
      <div className="overflow-x-auto">
        <table className="w-full min-w-[640px] text-sm">
          <thead className="bg-slate-50">
            <tr>
              {headers.map((header) => (
                <th key={header} className="px-4 py-3 text-left font-semibold text-slate-600">
                  {header}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.length ? (
              rows
            ) : (
              <tr>
                <td colSpan={headers.length} className="px-4 py-10 text-center text-slate-500">
                  {empty}
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}

export default function AdminPage() {
  const [query, setQuery] = useState("");
  const [loadingCustomers, setLoadingCustomers] = useState(false);
  const [customers, setCustomers] = useState([]);
  const [selectedTenantId, setSelectedTenantId] = useState("");
  const [selectedMonth, setSelectedMonth] = useState(new Date().toISOString().slice(0, 7));
  const [details, setDetails] = useState(null);
  const [usage, setUsage] = useState(null);
  const [subscriptions, setSubscriptions] = useState([]);
  const [invoices, setInvoices] = useState([]);
  const [members, setMembers] = useState([]);
  const [activity, setActivity] = useState([]);
  const [plans, setPlans] = useState([]);
  const [assignPlanCode, setAssignPlanCode] = useState("");
  const [assignCycle, setAssignCycle] = useState("monthly");
  const [assignStatus, setAssignStatus] = useState("active");
  const [trialDays, setTrialDays] = useState(14);
  const [assigningPlan, setAssigningPlan] = useState(false);
  const [tenantFeatures, setTenantFeatures] = useState(DEFAULT_FEATURES);
  const [savingFeatures, setSavingFeatures] = useState(false);
  const [ownerWorkspaceLoading, setOwnerWorkspaceLoading] = useState(false);
  const [ownerWorkspace, setOwnerWorkspace] = useState({
    invoices: [],
    tenantSettings: [],
    security: { summary: {}, loginHistory: [], sessionsByUser: [], auditEvents: [] },
  });
  const [companySettings, setCompanySettings] = useState({
    billingEmail: "",
    billingPhone: "",
    publicApiEnabled: false,
    apiUsername: "",
    apiPassword: "",
    apiKey: "",
    apiIpWhitelist: "",
    ownerGroupSmsProviderRoute: "tata",
    taxRatePercent: 18,
    isTaxExempt: false,
    isReverseCharge: false,
  });
  const [savingCompanySettings, setSavingCompanySettings] = useState(false);
  const [platformUsers, setPlatformUsers] = useState([]);
  const [selectedUserId, setSelectedUserId] = useState("");
  const [userTenantReport, setUserTenantReport] = useState(null);
  const [docViewer, setDocViewer] = useState({ open: false, type: "sms" });

  const loadCustomers = useCallback(async (search = "") => {
    try {
      setLoadingCustomers(true);
      const data = await getPlatformCustomers(search);
      const rows = Array.isArray(data) ? data : [];
      setCustomers(rows);
      setSelectedTenantId((prev) => prev || rows[0]?.tenantId || "");
    } catch (error) {
      toast.error(error?.message || "Failed to load platform customers");
    } finally {
      setLoadingCustomers(false);
    }
  }, []);

  const loadTenantDetails = useCallback(async () => {
    if (!selectedTenantId) return;
    try {
      const [
        customerDetails,
        customerCompanySettings,
        customerUsage,
        customerSubscriptions,
        customerInvoices,
        customerMembers,
        customerActivity,
        customerFeatures,
      ] = await Promise.all([
        getPlatformCustomerDetails(selectedTenantId),
        getPlatformCustomerCompanySettings(selectedTenantId).catch(() => null),
        getPlatformCustomerUsage(selectedTenantId, selectedMonth),
        getPlatformCustomerSubscriptions(selectedTenantId),
        getPlatformCustomerInvoices(selectedTenantId),
        getPlatformCustomerMembers(selectedTenantId),
        getPlatformCustomerActivity(selectedTenantId, 100),
        getPlatformCustomerFeatures(selectedTenantId).catch(() => DEFAULT_FEATURES),
      ]);

      setDetails(customerDetails || null);
      setCompanySettings({
        billingEmail: customerCompanySettings?.billingEmail || "",
        billingPhone: customerCompanySettings?.billingPhone || "",
        publicApiEnabled: !!customerCompanySettings?.publicApiEnabled,
        apiUsername: customerCompanySettings?.apiUsername || "",
        apiPassword: customerCompanySettings?.apiPassword || "",
        apiKey: customerCompanySettings?.apiKey || "",
        apiIpWhitelist: customerCompanySettings?.apiIpWhitelist || "",
        ownerGroupSmsProviderRoute: customerCompanySettings?.ownerGroupSmsProviderRoute || "tata",
        taxRatePercent: Number(customerCompanySettings?.taxRatePercent ?? 18),
        isTaxExempt: !!customerCompanySettings?.isTaxExempt,
        isReverseCharge: !!customerCompanySettings?.isReverseCharge,
      });
      setUsage(customerUsage || null);
      setSubscriptions(Array.isArray(customerSubscriptions) ? customerSubscriptions : []);
      setInvoices(Array.isArray(customerInvoices) ? customerInvoices : []);
      setMembers(Array.isArray(customerMembers) ? customerMembers : []);
      setActivity(Array.isArray(customerActivity) ? customerActivity : []);
      setTenantFeatures({
        smsGatewayReportEnabled: !!customerFeatures?.smsGatewayReportEnabled,
      });
    } catch (error) {
      toast.error(error?.message || "Failed to load tenant control data");
    }
  }, [selectedMonth, selectedTenantId]);

  useEffect(() => {
    loadCustomers("");
  }, [loadCustomers]);

  useEffect(() => {
    (async () => {
      try {
        const data = await listPlatformBillingPlans();
        const activePlans = Array.isArray(data) ? data.filter((plan) => plan.isActive) : [];
        setPlans(activePlans);
        setAssignPlanCode((prev) => prev || activePlans[0]?.code || "");
      } catch {
        setPlans([]);
      }
    })();
  }, []);

  useEffect(() => {
    (async () => {
      try {
        const data = await getPlatformUsers("", true);
        const users = Array.isArray(data) ? data : [];
        setPlatformUsers(users);
        setSelectedUserId((prev) => prev || users[0]?.userId || "");
      } catch {
        setPlatformUsers([]);
      }
    })();
  }, []);

  useEffect(() => {
    loadTenantDetails();
  }, [loadTenantDetails]);

  useEffect(() => {
    if (!selectedUserId) return;
    getPlatformUserTenants(selectedUserId)
      .then((data) => setUserTenantReport(data || null))
      .catch(() => setUserTenantReport(null));
  }, [selectedUserId]);

  const selectedOwner = useMemo(
    () => platformUsers.find((user) => user.userId === selectedUserId) || null,
    [platformUsers, selectedUserId],
  );

  const userCompanyRows = useMemo(() => {
    return (userTenantReport?.groups || []).flatMap((group) => group.companies || []);
  }, [userTenantReport]);

  const ownerTenantIds = useMemo(
    () => new Set(userCompanyRows.map((company) => company.tenantId)),
    [userCompanyRows],
  );

  const ownerCustomers = useMemo(() => {
    const rows = ownerTenantIds.size
      ? customers.filter((item) => ownerTenantIds.has(item.tenantId))
      : customers;
    const q = query.trim().toLowerCase();
    if (!q) return rows;
    return rows.filter((item) =>
      [item.tenantName, item.tenantSlug, item.companyName, item.ownerEmail, item.ownerName]
        .filter(Boolean)
        .some((value) => String(value).toLowerCase().includes(q)),
    );
  }, [customers, ownerTenantIds, query]);

  const ownerTotals = useMemo(() => {
    const tenantRows = ownerTenantIds.size ? customers.filter((item) => ownerTenantIds.has(item.tenantId)) : [];
    const tenants = tenantRows.length;
    const users = tenantRows.reduce((acc, item) => acc + Number(item.users || 0), 0);
    const activeUsers = tenantRows.reduce((acc, item) => acc + Number(item.activeUsers || 0), 0);
    const revenue = tenantRows.reduce((acc, item) => acc + Number(item.totalRevenue || 0), 0);
    const activePlans = tenantRows.filter((item) => {
      const status = String(item.subscriptionStatus || item.billingStatus || "").toLowerCase();
      return status === "active" || status === "trial" || status === "trialing";
    }).length;
    const invoiceCount = tenantRows.reduce((acc, item) => acc + Number(item.invoiceCount || 0), 0);
    return { tenants, users, activeUsers, revenue, activePlans, invoiceCount };
  }, [customers, ownerTenantIds]);

  const selectedCustomer = useMemo(
    () => ownerCustomers.find((item) => item.tenantId === selectedTenantId) || customers.find((item) => item.tenantId === selectedTenantId) || null,
    [ownerCustomers, customers, selectedTenantId],
  );

  useEffect(() => {
    if (!ownerCustomers.length) return;
    const validTenant = ownerCustomers.some((item) => item.tenantId === selectedTenantId);
    if (!validTenant) setSelectedTenantId(ownerCustomers[0].tenantId);
  }, [ownerCustomers, selectedTenantId]);

  useEffect(() => {
    let ignore = false;
    if (!selectedUserId || !ownerCustomers.length) {
      setOwnerWorkspace({
        invoices: [],
        tenantSettings: [],
        security: { summary: {}, loginHistory: [], sessionsByUser: [], auditEvents: [] },
      });
      return undefined;
    }

    (async () => {
      try {
        setOwnerWorkspaceLoading(true);
        const tenantRows = await Promise.all(
          ownerCustomers.map(async (customer) => {
            const [invoiceRows, companyRow, featureRow] = await Promise.all([
              getPlatformCustomerInvoices(customer.tenantId).catch(() => []),
              getPlatformCustomerCompanySettings(customer.tenantId).catch(() => null),
              getPlatformCustomerFeatures(customer.tenantId).catch(() => DEFAULT_FEATURES),
            ]);
            return {
              customer,
              invoices: Array.isArray(invoiceRows) ? invoiceRows : [],
              company: companyRow,
              features: featureRow || DEFAULT_FEATURES,
            };
          }),
        );

        const security = await getPlatformSecurityReport({
          userId: selectedUserId,
          limit: 200,
        }).catch(() => ({ summary: {}, loginHistory: [], sessionsByUser: [], auditEvents: [] }));

        if (ignore) return;

        const invoices = tenantRows
          .flatMap(({ customer, invoices: invoiceRows }) =>
            invoiceRows.map((invoice) => ({
              ...invoice,
              tenantId: customer.tenantId,
              tenantName: customer.tenantName,
              tenantSlug: customer.tenantSlug,
              companyName: customer.companyName,
            })),
          )
          .sort((a, b) => new Date(b.createdAtUtc || 0).getTime() - new Date(a.createdAtUtc || 0).getTime());

        const tenantSettings = tenantRows.map(({ customer, company, features }) => ({
          tenantId: customer.tenantId,
          tenantName: customer.tenantName,
          tenantSlug: customer.tenantSlug,
          companyName: customer.companyName,
          planName: customer.planName || "No Plan",
          subscriptionStatus: customer.subscriptionStatus || "none",
          ownerGroupSmsProviderRoute: company?.ownerGroupSmsProviderRoute || "tata",
          publicApiEnabled: company?.publicApiEnabled ?? true,
          billingEmail: company?.billingEmail || "",
          billingPhone: company?.billingPhone || "",
          taxRatePercent: Number(company?.taxRatePercent ?? 18),
          isTaxExempt: !!company?.isTaxExempt,
          isReverseCharge: !!company?.isReverseCharge,
          smsGatewayReportEnabled: !!features?.smsGatewayReportEnabled,
        }));

        setOwnerWorkspace({
          invoices,
          tenantSettings,
          security,
        });
      } finally {
        if (!ignore) setOwnerWorkspaceLoading(false);
      }
    })();

    return () => {
      ignore = true;
    };
  }, [ownerCustomers, selectedUserId]);

  const generateToken = (prefix, length) => {
    const chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
    let value = prefix;
    for (let i = 0; i < length; i += 1) value += chars.charAt(Math.floor(Math.random() * chars.length));
    return value;
  };

  const ownerBillingSummary = useMemo(() => {
    const invoiceRows = ownerWorkspace.invoices || [];
    const tenantSettings = ownerWorkspace.tenantSettings || [];
    const paidTotal = invoiceRows
      .filter((row) => String(row.status || "").toLowerCase() === "paid")
      .reduce((acc, row) => acc + Number(row.total || 0), 0);
    const openTotal = invoiceRows
      .filter((row) => !["paid", "settled"].includes(String(row.status || "").toLowerCase()))
      .reduce((acc, row) => acc + Number(row.total || 0), 0);
    const monthlyExposure = ownerCustomers.reduce((acc, row) => acc + Number(row.monthlyPrice || 0), 0);
    return {
      paidTotal,
      openTotal,
      monthlyExposure,
      invoices: invoiceRows.length,
      taxExemptTenants: tenantSettings.filter((row) => row.isTaxExempt).length,
      reverseChargeTenants: tenantSettings.filter((row) => row.isReverseCharge).length,
    };
  }, [ownerCustomers, ownerWorkspace.invoices, ownerWorkspace.tenantSettings]);

  const ownerMessagingSummary = useMemo(() => {
    const rows = ownerWorkspace.tenantSettings || [];
    return {
      tata: rows.filter((row) => row.ownerGroupSmsProviderRoute === "tata").length,
      equence: rows.filter((row) => row.ownerGroupSmsProviderRoute === "equence").length,
      publicApi: rows.filter((row) => row.publicApiEnabled).length,
      smsReportEnabled: rows.filter((row) => row.smsGatewayReportEnabled).length,
      whatsappReady: ownerCustomers.filter((row) => ["active", "trial", "trialing"].includes(String(row.subscriptionStatus || "").toLowerCase())).length,
    };
  }, [ownerCustomers, ownerWorkspace.tenantSettings]);

  const ownerSecuritySummary = useMemo(() => {
    const security = ownerWorkspace.security || {};
    const loginHistory = security.loginHistory || [];
    const auditEvents = security.auditEvents || [];
    const activeSessions = loginHistory.filter((row) => !row.revokedAtUtc && (!row.expiresAtUtc || new Date(row.expiresAtUtc) > new Date())).length;
    const suspiciousLogins = loginHistory.filter((row) => row.isSuspicious || row.ipPolicyStatus === "blocked" || row.ipPolicyStatus === "not_allowlisted").length;
    const highSeverity = auditEvents.filter((row) => String(row.severity || "").toLowerCase() === "high").length;
    return {
      loginCount: loginHistory.length,
      activeSessions,
      suspiciousLogins,
      highSeverity,
    };
  }, [ownerWorkspace.security]);

  const copyValue = async (label, value) => {
    if (!value) {
      toast.error(`${label} is empty.`);
      return;
    }
    await navigator.clipboard.writeText(value);
    toast.success(`${label} copied.`);
  };

  return (
    <div className="space-y-6">
      <section className="overflow-hidden rounded-[28px] border border-slate-200 bg-[radial-gradient(circle_at_top_left,_rgba(251,146,60,0.18),_transparent_28%),linear-gradient(135deg,#fff7ed_0%,#ffffff_46%,#f8fafc_100%)] p-6 shadow-sm">
        <div className="flex flex-col gap-6 xl:flex-row xl:items-end xl:justify-between">
          <div className="max-w-3xl">
            <Badge className="border-orange-200 bg-white/80 px-3 py-1 text-[11px] font-semibold uppercase tracking-[0.22em] text-orange-600 shadow-sm">
              Platform Owner Control
            </Badge>
            <h1 className="mt-4 text-3xl font-bold tracking-tight text-slate-950 md:text-4xl">Operate tenants, revenue, plans and risk from one workspace.</h1>
            <p className="mt-3 max-w-2xl text-sm leading-6 text-slate-600 md:text-base">
              This admin console is the SaaS command layer for platform owners. Review commercial posture, inspect tenant state, assign plans, audit membership and apply tenant-level controls without switching pages.
            </p>
          </div>
          <div className="flex flex-wrap gap-3">
            <Button variant="outline" className="h-11 rounded-xl border-slate-300 bg-white/80 px-5" onClick={() => setDocViewer({ open: true, type: "sms" })}>
              <BookOpenText className="mr-2 h-4 w-4" />
              API Docs
            </Button>
            <Button variant="outline" className="h-11 rounded-xl border-slate-300 bg-white/80 px-5" onClick={() => loadCustomers(query)} disabled={loadingCustomers}>
              <RefreshCcw className={`mr-2 h-4 w-4 ${loadingCustomers ? "animate-spin" : ""}`} />
              Refresh portfolio
            </Button>
            <Button className="h-11 rounded-xl bg-orange-500 px-5 hover:bg-orange-600" onClick={() => window.location.assign("/dashboard/platform-settings?tab=billing-plans")}>
              Billing Plans
              <ArrowUpRight className="ml-2 h-4 w-4" />
            </Button>
          </div>
        </div>
      </section>

      <Card className="border-slate-200 shadow-sm">
        <CardContent className="pt-6">
          <div className="grid gap-4 xl:grid-cols-[320px_minmax(0,1fr)]">
            <div className="space-y-3">
              <Label>Owner Workspace</Label>
              <Select value={selectedUserId} onValueChange={setSelectedUserId}>
                <SelectTrigger className="h-12 rounded-xl">
                  <SelectValue placeholder="Select owner" />
                </SelectTrigger>
                <SelectContent>
                  {platformUsers.map((user) => (
                    <SelectItem key={user.userId} value={user.userId}>
                      {user.name} ({user.email})
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <p className="text-xs text-slate-500">
                Pick one owner and the whole page will scope to that owner’s tenants, billing, invoices, routes, and activity.
              </p>
            </div>
            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
              <div className="rounded-2xl border border-slate-200 bg-slate-50/70 p-4">
                <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">Selected Owner</p>
                <p className="mt-2 text-lg font-bold text-slate-950">{selectedOwner?.name || "No owner selected"}</p>
                <p className="text-sm text-slate-500">{selectedOwner?.email || "Select an owner to load workspace"}</p>
              </div>
              <div className="rounded-2xl border border-slate-200 bg-slate-50/70 p-4">
                <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">Owner Groups</p>
                <p className="mt-2 text-lg font-bold text-slate-950">{Number(userTenantReport?.ownerGroupCount || 0).toLocaleString()}</p>
                <p className="text-sm text-slate-500">{Number(ownerTotals.tenants || 0).toLocaleString()} tenants mapped</p>
              </div>
              <div className="rounded-2xl border border-slate-200 bg-slate-50/70 p-4">
                <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">Tenant Slug Coverage</p>
                <p className="mt-2 text-lg font-bold text-slate-950">{ownerCustomers.length.toLocaleString()}</p>
                <p className="text-sm text-slate-500">Filtered workspaces for this owner</p>
              </div>
              <div className="rounded-2xl border border-slate-200 bg-slate-50/70 p-4">
                <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">Workspace Mode</p>
                <p className="mt-2 text-lg font-bold text-slate-950">Owner Focus</p>
                <p className="text-sm text-slate-500">Billing, invoices, settings and audit on one page</p>
              </div>
            </div>
          </div>
        </CardContent>
      </Card>

      <div className="grid gap-4 md:grid-cols-2 2xl:grid-cols-5">
        <KpiCard title="Owner Tenants" value={ownerTotals.tenants.toLocaleString()} hint="Workspaces under selected owner" icon={Building2} />
        <KpiCard title="Owner Seats" value={ownerTotals.users.toLocaleString()} hint={`${ownerTotals.activeUsers.toLocaleString()} active seats`} icon={Users} tone="blue" />
        <KpiCard title="Owner Revenue" value={formatMoney(ownerTotals.revenue)} hint={`${ownerTotals.activePlans.toLocaleString()} active subscriptions`} icon={BadgeIndianRupee} tone="emerald" />
        <KpiCard title="Invoices" value={ownerTotals.invoiceCount.toLocaleString()} hint="Invoices generated across owner tenants" icon={FileText} tone="violet" />
        <KpiCard title="Tenant Controls" value={tenantFeatures.smsGatewayReportEnabled ? "Reports on" : "Reports off"} hint="Selected tenant overrides" icon={ShieldCheck} tone="orange" />
      </div>

      <div className="grid gap-6 xl:grid-cols-[1.35fr_0.95fr]">
        <Card className="border-slate-200 shadow-sm">
          <CardHeader className="pb-4">
            <div className="flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
              <div>
                <CardTitle className="text-xl">Owner Tenant Portfolio</CardTitle>
                <CardDescription>Select one owner above, then inspect that owner’s tenants, lifecycle, billing, and support posture from this page.</CardDescription>
              </div>
              <div className="flex w-full flex-col gap-3 md:flex-row lg:w-auto">
                <div className="relative min-w-[260px]">
                  <Filter className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
                  <Input
                    value={query}
                    onChange={(event) => setQuery(event.target.value)}
                    placeholder="Search within selected owner’s tenants"
                    className="h-11 rounded-xl border-slate-300 pl-9"
                  />
                </div>
                <Button variant="outline" className="h-11 rounded-xl" onClick={() => loadCustomers("")} disabled={loadingCustomers}>
                  Refresh data
                </Button>
              </div>
            </div>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="grid gap-3 md:grid-cols-[minmax(0,1fr)_280px]">
              <SectionTable
                headers={["Tenant", "Owner", "Plan", "Status", "Revenue"]}
                empty="No tenants found for the selected owner."
                rows={ownerCustomers.map((item) => {
                  const active = item.tenantId === selectedTenantId;
                  return (
                    <tr
                      key={item.tenantId}
                      className={`cursor-pointer border-t border-slate-100 transition ${active ? "bg-orange-50/70" : "hover:bg-slate-50"}`}
                      onClick={() => setSelectedTenantId(item.tenantId)}
                    >
                      <td className="px-4 py-3">
                        <div className="font-semibold text-slate-900">{item.tenantName}</div>
                        <div className="text-xs text-slate-500">{item.tenantSlug}</div>
                      </td>
                      <td className="px-4 py-3 text-slate-700">{item.ownerName || "-"}</td>
                      <td className="px-4 py-3 text-slate-700">{item.planName || "No Plan"}</td>
                      <td className="px-4 py-3">
                        <Badge variant="outline" className="border-slate-200 bg-white capitalize text-slate-700">
                          {item.billingStatus || "unknown"}
                        </Badge>
                      </td>
                      <td className="px-4 py-3 font-medium text-slate-900">{formatMoney(item.totalRevenue || 0)}</td>
                    </tr>
                  );
                })}
              />

              <div className="rounded-2xl border border-slate-200 bg-slate-50/70 p-4">
                <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">Selected Tenant</p>
                {selectedCustomer ? (
                  <div className="mt-3 space-y-4">
                    <div>
                      <p className="text-lg font-bold text-slate-950">{selectedCustomer.tenantName}</p>
                      <p className="text-sm text-slate-500">{selectedCustomer.tenantSlug}</p>
                    </div>
                    <div className="space-y-3 text-sm">
                      <div className="flex items-center justify-between gap-3 rounded-xl border border-slate-200 bg-white px-4 py-3">
                        <span className="text-slate-500">Owner</span>
                        <span className="font-medium text-slate-900">{selectedCustomer.ownerName || "-"}</span>
                      </div>
                      <div className="flex items-center justify-between gap-3 rounded-xl border border-slate-200 bg-white px-4 py-3">
                        <span className="text-slate-500">Plan</span>
                        <span className="font-medium text-slate-900">{selectedCustomer.planName || "No Plan"}</span>
                      </div>
                      <div className="flex items-center justify-between gap-3 rounded-xl border border-slate-200 bg-white px-4 py-3">
                        <span className="text-slate-500">Users</span>
                        <span className="font-medium text-slate-900">{Number(selectedCustomer.users || 0).toLocaleString()}</span>
                      </div>
                      <div className="flex items-center justify-between gap-3 rounded-xl border border-slate-200 bg-white px-4 py-3">
                        <span className="text-slate-500">Revenue</span>
                        <span className="font-medium text-slate-900">{formatMoney(selectedCustomer.totalRevenue || 0)}</span>
                      </div>
                    </div>
                  </div>
                ) : (
                  <p className="mt-3 text-sm text-slate-500">Select a tenant to view its control panel.</p>
                )}
              </div>
            </div>
          </CardContent>
        </Card>

        <div className="space-y-6">
          <Card className="border-slate-200 shadow-sm">
            <CardHeader>
              <CardTitle className="text-xl">Commercial Control</CardTitle>
              <CardDescription>Assign plans and keep each tenant on the correct billing cycle.</CardDescription>
            </CardHeader>
            <CardContent className="space-y-4">
              <div className="space-y-2">
                <Label>Plan</Label>
                <Select value={assignPlanCode} onValueChange={setAssignPlanCode}>
                  <SelectTrigger className="h-11 rounded-xl"><SelectValue placeholder="Select plan" /></SelectTrigger>
                  <SelectContent>
                    {plans.map((plan) => (
                      <SelectItem key={plan.id} value={plan.code}>{plan.name} ({plan.code})</SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              <div className="space-y-2">
                <Label>Billing Cycle</Label>
                <Select value={assignCycle} onValueChange={setAssignCycle}>
                  <SelectTrigger className="h-11 rounded-xl"><SelectValue /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value="monthly">Monthly</SelectItem>
                    <SelectItem value="yearly">Yearly</SelectItem>
                    <SelectItem value="lifetime">Lifetime</SelectItem>
                  </SelectContent>
                </Select>
              </div>
              <div className="grid gap-4 md:grid-cols-2">
                <div className="space-y-2">
                  <Label>Subscription Status</Label>
                  <Select value={assignStatus} onValueChange={setAssignStatus}>
                    <SelectTrigger className="h-11 rounded-xl"><SelectValue /></SelectTrigger>
                    <SelectContent>
                      <SelectItem value="active">Active</SelectItem>
                      <SelectItem value="trial">Trial</SelectItem>
                      <SelectItem value="suspended">Suspended</SelectItem>
                    </SelectContent>
                  </Select>
                </div>
                {assignStatus === "trial" ? (
                  <div className="space-y-2">
                    <Label>Trial Days</Label>
                    <Input
                      type="number"
                      min="1"
                      max="365"
                      value={trialDays}
                      onChange={(event) => setTrialDays(Number(event.target.value || 14))}
                      className="h-11 rounded-xl"
                    />
                  </div>
                ) : null}
              </div>
              <Button
                className="h-11 w-full rounded-xl bg-orange-500 hover:bg-orange-600"
                disabled={!selectedTenantId || !assignPlanCode || assigningPlan}
                onClick={async () => {
                  try {
                    setAssigningPlan(true);
                    await assignPlatformCustomerPlan(selectedTenantId, {
                      planCode: assignPlanCode,
                      billingCycle: assignCycle,
                      status: assignStatus,
                      trialDays: assignStatus === "trial" ? trialDays : 0,
                      resetStartDate: true,
                    });
                    toast.success(assignStatus === "trial" ? "Trial plan assigned successfully." : "Plan assigned successfully.");
                    await Promise.all([loadCustomers(query), loadTenantDetails()]);
                  } catch (error) {
                    toast.error(error?.message || "Failed to assign plan.");
                  } finally {
                    setAssigningPlan(false);
                  }
                }}
              >
                <CreditCard className="mr-2 h-4 w-4" />
                {assigningPlan ? "Applying plan..." : "Assign Plan"}
              </Button>
            </CardContent>
          </Card>

          <Card className="border-slate-200 shadow-sm">
            <CardHeader>
              <CardTitle className="text-xl">Tenant Feature Controls</CardTitle>
              <CardDescription>Apply platform-managed feature flags at tenant level.</CardDescription>
            </CardHeader>
            <CardContent className="space-y-4">
              <div className="rounded-2xl border border-slate-200 bg-slate-50/70 p-4">
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <p className="font-semibold text-slate-950">SMS Gateway Report Visibility</p>
                    <p className="mt-1 text-sm text-slate-500">Allow tenant users to access their own outbound SMS request and delivery reporting.</p>
                  </div>
                  <Badge className={tenantFeatures.smsGatewayReportEnabled ? "bg-emerald-100 text-emerald-700" : "bg-slate-100 text-slate-600"}>
                    {tenantFeatures.smsGatewayReportEnabled ? "Enabled" : "Disabled"}
                  </Badge>
                </div>
                <Button
                  variant={tenantFeatures.smsGatewayReportEnabled ? "outline" : "default"}
                  className={`mt-4 h-10 rounded-xl ${tenantFeatures.smsGatewayReportEnabled ? "border-slate-300" : "bg-orange-500 hover:bg-orange-600"}`}
                  disabled={!selectedTenantId || savingFeatures}
                  onClick={async () => {
                    try {
                      setSavingFeatures(true);
                      const nextValue = !tenantFeatures.smsGatewayReportEnabled;
                      const updated = await savePlatformCustomerFeatures(selectedTenantId, {
                        smsGatewayReportEnabled: nextValue,
                      });
                      setTenantFeatures({
                        smsGatewayReportEnabled: !!updated?.smsGatewayReportEnabled,
                      });
                      toast.success(`SMS report ${nextValue ? "enabled" : "disabled"} for tenant.`);
                    } catch (error) {
                      toast.error(error?.message || "Failed to update tenant feature.");
                    } finally {
                      setSavingFeatures(false);
                    }
                  }}
                >
                  <ShieldCheck className="mr-2 h-4 w-4" />
                  {savingFeatures ? "Saving..." : tenantFeatures.smsGatewayReportEnabled ? "Disable access" : "Enable access"}
                </Button>
              </div>
            </CardContent>
          </Card>

          <Card className="border-slate-200 shadow-sm">
            <CardHeader>
              <CardTitle className="text-xl">Documentation Center</CardTitle>
              <CardDescription>Platform-grade SMS and WhatsApp API references for admin operations, onboarding, and partner support.</CardDescription>
            </CardHeader>
            <CardContent className="space-y-4">
              <div className="rounded-2xl border border-slate-200 bg-slate-50 p-4">
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <p className="font-medium text-slate-950">SMS API Reference</p>
                    <p className="mt-1 text-sm text-slate-500">Public SMS send, DLT mapping, Tata request model, delivery reporting, and rollout checklist.</p>
                  </div>
                  <FileText className="h-5 w-5 text-orange-500" />
                </div>
                <div className="mt-4 flex flex-wrap gap-2">
                  <Button className="bg-orange-500 hover:bg-orange-600" onClick={() => setDocViewer({ open: true, type: "sms" })}>Read in App</Button>
                  <Button variant="outline" onClick={() => window.open("/docs/sms-api-reference.html", "_blank", "noopener,noreferrer")}>Open Full Page</Button>
                </div>
              </div>

              <div className="rounded-2xl border border-slate-200 bg-slate-50 p-4">
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <p className="font-medium text-slate-950">WhatsApp API Reference</p>
                    <p className="mt-1 text-sm text-slate-500">Messaging, templates, automation, flows, webhooks, diagnostics, and production operations.</p>
                  </div>
                  <FileText className="h-5 w-5 text-sky-500" />
                </div>
                <div className="mt-4 flex flex-wrap gap-2">
                  <Button className="bg-orange-500 hover:bg-orange-600" onClick={() => setDocViewer({ open: true, type: "whatsapp" })}>Read in App</Button>
                  <Button variant="outline" onClick={() => window.open("/docs/whatsapp-api-reference.html", "_blank", "noopener,noreferrer")}>Open Full Page</Button>
                </div>
              </div>
            </CardContent>
          </Card>

          <Card className="border-slate-200 shadow-sm">
            <CardHeader>
              <CardTitle className="text-xl">Tenant Tax Profile</CardTitle>
              <CardDescription>Control GST rate and invoice tax handling for the selected tenant from the platform side.</CardDescription>
            </CardHeader>
            <CardContent className="space-y-4">
              <div className="grid gap-4 md:grid-cols-2">
                <div className="space-y-2">
                  <Label>Billing Email</Label>
                  <Input
                    value={companySettings.billingEmail}
                    onChange={(event) => setCompanySettings((prev) => ({ ...prev, billingEmail: event.target.value }))}
                    placeholder="accounts@tenant.com"
                    disabled={!selectedTenantId}
                  />
                </div>
                <div className="space-y-2">
                  <Label>Billing Phone</Label>
                  <Input
                    value={companySettings.billingPhone}
                    onChange={(event) => setCompanySettings((prev) => ({ ...prev, billingPhone: event.target.value }))}
                    placeholder="+91..."
                    disabled={!selectedTenantId}
                  />
                </div>
                <div className="space-y-2">
                  <Label>Tax Rate %</Label>
                  <Input
                    type="number"
                    min="0"
                    max="100"
                    step="0.01"
                    value={companySettings.taxRatePercent}
                    onChange={(event) => setCompanySettings((prev) => ({ ...prev, taxRatePercent: Number(event.target.value || 0) }))}
                    disabled={!selectedTenantId}
                  />
                </div>
                <div className="rounded-2xl border border-slate-200 bg-slate-50/70 p-4 space-y-4">
                  <div className="flex items-center justify-between gap-3">
                    <div>
                      <p className="font-medium text-slate-900">Tax Exempt</p>
                      <p className="text-xs text-slate-500">No GST will be applied on invoices.</p>
                    </div>
                    <Switch
                      checked={!!companySettings.isTaxExempt}
                      onCheckedChange={(value) => setCompanySettings((prev) => ({ ...prev, isTaxExempt: !!value }))}
                      disabled={!selectedTenantId}
                    />
                  </div>
                  <div className="flex items-center justify-between gap-3">
                    <div>
                      <p className="font-medium text-slate-900">Reverse Charge</p>
                      <p className="text-xs text-slate-500">Invoices mark GST as reverse charge instead of collecting it.</p>
                    </div>
                    <Switch
                      checked={!!companySettings.isReverseCharge}
                      onCheckedChange={(value) => setCompanySettings((prev) => ({ ...prev, isReverseCharge: !!value }))}
                      disabled={!selectedTenantId}
                    />
                  </div>
                </div>
              </div>
              <div className="rounded-2xl border border-slate-200 bg-slate-50/70 p-4 space-y-4">
                <div>
                  <p className="font-medium text-slate-900">Tenant Public API</p>
                  <p className="text-xs text-slate-500">Tenant API credentials are managed from the tenant integrations page. Platform admin no longer sees or edits those secrets here.</p>
                </div>
                <div className="grid gap-4 md:grid-cols-2">
                  <div className="space-y-2">
                    <Label>IP Whitelist</Label>
                    <Input
                      value={companySettings.apiIpWhitelist}
                      onChange={(event) => setCompanySettings((prev) => ({ ...prev, apiIpWhitelist: event.target.value }))}
                      placeholder="203.0.113.10, 198.51.100.0/24"
                      disabled={!selectedTenantId}
                    />
                  </div>
                  <div className="space-y-2">
                    <Label>Owner Group SMS Route</Label>
                    <Select
                      value={companySettings.ownerGroupSmsProviderRoute || "tata"}
                      onValueChange={(value) => setCompanySettings((prev) => ({ ...prev, ownerGroupSmsProviderRoute: value }))}
                      disabled={!selectedTenantId}
                    >
                      <SelectTrigger>
                        <SelectValue placeholder="Select SMS provider" />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="tata">Tata</SelectItem>
                        <SelectItem value="equence">Equence</SelectItem>
                      </SelectContent>
                    </Select>
                    <p className="text-xs text-slate-500">All tenants under this owner group will use the selected SMS route by default.</p>
                  </div>
                </div>
              </div>
              <Button
                className="h-11 w-full rounded-xl bg-orange-500 hover:bg-orange-600"
                disabled={!selectedTenantId || savingCompanySettings}
                onClick={async () => {
                  try {
                    setSavingCompanySettings(true);
                    const updated = await savePlatformCustomerCompanySettings(selectedTenantId, companySettings);
                    setCompanySettings({
                      billingEmail: updated?.billingEmail || "",
                      billingPhone: updated?.billingPhone || "",
                      publicApiEnabled: !!updated?.publicApiEnabled,
                      apiUsername: updated?.apiUsername || "",
                      apiPassword: updated?.apiPassword || "",
                      apiKey: updated?.apiKey || "",
                      apiIpWhitelist: updated?.apiIpWhitelist || "",
                      ownerGroupSmsProviderRoute: updated?.ownerGroupSmsProviderRoute || "tata",
                      taxRatePercent: Number(updated?.taxRatePercent ?? 18),
                      isTaxExempt: !!updated?.isTaxExempt,
                      isReverseCharge: !!updated?.isReverseCharge,
                    });
                    toast.success("Tenant tax profile updated.");
                  } catch (error) {
                    toast.error(error?.message || "Failed to update tenant tax profile.");
                  } finally {
                    setSavingCompanySettings(false);
                  }
                }}
              >
                {savingCompanySettings ? "Saving..." : "Save Tax Profile"}
              </Button>
            </CardContent>
          </Card>
        </div>
      </div>

      <Card className="border-slate-200 shadow-sm">
        <CardHeader>
          <div className="flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
            <div>
              <CardTitle className="text-xl">Owner to Tenant Mapping</CardTitle>
              <CardDescription>Everything below is already scoped to the selected owner. Use this section to verify owner-group mapping and company coverage.</CardDescription>
            </div>
            <div className="min-w-[280px]">
              <Select value={selectedUserId} onValueChange={setSelectedUserId}>
                <SelectTrigger className="h-11 rounded-xl"><SelectValue placeholder="Select user" /></SelectTrigger>
                <SelectContent>
                  {platformUsers.map((user) => (
                    <SelectItem key={user.userId} value={user.userId}>{user.name} ({user.email})</SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          </div>
        </CardHeader>
        <CardContent>
          <SectionTable
            headers={["Company", "Tenant", "Role", "Plan", "Billing Status"]}
            empty="No company mappings available for the selected user."
            rows={userCompanyRows.map((company) => (
              <tr key={company.tenantId} className="border-t border-slate-100">
                <td className="px-4 py-3 font-medium text-slate-900">{company.companyName || "-"}</td>
                <td className="px-4 py-3 text-slate-700">{company.tenantName} ({company.tenantSlug})</td>
                <td className="px-4 py-3 text-slate-700">{company.role || "-"}</td>
                <td className="px-4 py-3 text-slate-700">{company.planName || "No Plan"}</td>
                <td className="px-4 py-3"><Badge variant="outline" className="capitalize">{company.billingStatus || "unknown"}</Badge></td>
              </tr>
            ))}
          />
        </CardContent>
      </Card>

      <div className="grid gap-6 xl:grid-cols-[1.1fr_0.9fr]">
        <Card className="border-slate-200 shadow-sm">
          <CardHeader>
            <div className="flex items-start justify-between gap-4">
              <div>
                <CardTitle className="text-xl">Owner Billing Summary</CardTitle>
                <CardDescription>Commercial posture for all tenants under the selected owner.</CardDescription>
              </div>
              <Button variant="outline" className="rounded-xl" onClick={() => window.location.assign("/dashboard/platform-settings?tab=billing-plans")}>
                Manage plans
                <ArrowUpRight className="ml-2 h-4 w-4" />
              </Button>
            </div>
          </CardHeader>
          <CardContent className="space-y-5">
            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
              <div className="rounded-2xl border border-slate-200 bg-slate-50/80 p-4">
                <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">Collected</p>
                <p className="mt-2 text-2xl font-bold text-slate-950">{formatMoney(ownerBillingSummary.paidTotal)}</p>
                <p className="text-sm text-slate-500">{ownerBillingSummary.invoices.toLocaleString()} invoices total</p>
              </div>
              <div className="rounded-2xl border border-slate-200 bg-slate-50/80 p-4">
                <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">Open Exposure</p>
                <p className="mt-2 text-2xl font-bold text-slate-950">{formatMoney(ownerBillingSummary.openTotal)}</p>
                <p className="text-sm text-slate-500">Unpaid or pending invoice total</p>
              </div>
              <div className="rounded-2xl border border-slate-200 bg-slate-50/80 p-4">
                <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">Monthly Exposure</p>
                <p className="mt-2 text-2xl font-bold text-slate-950">{formatMoney(ownerBillingSummary.monthlyExposure)}</p>
                <p className="text-sm text-slate-500">Based on current monthly plan pricing</p>
              </div>
              <div className="rounded-2xl border border-slate-200 bg-slate-50/80 p-4">
                <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">Tax Modes</p>
                <p className="mt-2 text-2xl font-bold text-slate-950">{ownerBillingSummary.taxExemptTenants.toLocaleString()} / {ownerBillingSummary.reverseChargeTenants.toLocaleString()}</p>
                <p className="text-sm text-slate-500">Tax-exempt / reverse-charge tenants</p>
              </div>
            </div>

            <SectionTable
              headers={["Invoice", "Tenant", "Date", "Amount", "Status"]}
              empty={ownerWorkspaceLoading ? "Loading owner invoices..." : "No invoices found for this owner."}
              rows={(ownerWorkspace.invoices || []).slice(0, 12).map((invoice) => (
                <tr key={invoice.id} className="border-t border-slate-100">
                  <td className="px-4 py-3 font-medium text-slate-900">{invoice.invoiceNo || invoice.id}</td>
                  <td className="px-4 py-3 text-slate-700">
                    <div>{invoice.companyName || invoice.tenantName}</div>
                    <div className="text-xs text-slate-500">{invoice.tenantSlug || "-"}</div>
                  </td>
                  <td className="px-4 py-3 text-slate-700">{formatDate(invoice.createdAtUtc)}</td>
                  <td className="px-4 py-3 text-slate-900">{formatMoney(invoice.total || 0)}</td>
                  <td className="px-4 py-3">
                    <Badge variant="outline" className="capitalize">{formatStatus(invoice.status)}</Badge>
                  </td>
                </tr>
              ))}
            />
          </CardContent>
        </Card>

        <div className="space-y-6">
          <Card className="border-slate-200 shadow-sm">
            <CardHeader>
              <CardTitle className="text-xl">Owner Messaging & Feature Controls</CardTitle>
              <CardDescription>Transport routing, tenant feature access, and public API posture across this owner’s portfolio.</CardDescription>
            </CardHeader>
            <CardContent className="space-y-5">
              <div className="grid gap-4 md:grid-cols-2">
                <div className="rounded-2xl border border-slate-200 bg-slate-50/80 p-4">
                  <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">SMS Route Split</p>
                  <p className="mt-2 text-2xl font-bold text-slate-950">{ownerMessagingSummary.tata.toLocaleString()} Tata / {ownerMessagingSummary.equence.toLocaleString()} Equence</p>
                  <p className="text-sm text-slate-500">Owner-group routing defaults currently applied.</p>
                </div>
                <div className="rounded-2xl border border-slate-200 bg-slate-50/80 p-4">
                  <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">Channel Access</p>
                  <p className="mt-2 text-2xl font-bold text-slate-950">{ownerMessagingSummary.publicApi.toLocaleString()} API / {ownerMessagingSummary.smsReportEnabled.toLocaleString()} reports</p>
                  <p className="text-sm text-slate-500">{ownerMessagingSummary.whatsappReady.toLocaleString()} tenants have active WhatsApp billing posture.</p>
                </div>
              </div>

              <SectionTable
                headers={["Tenant", "Plan", "SMS Route", "Public API", "SMS Report", "Tax Mode"]}
                empty={ownerWorkspaceLoading ? "Loading owner tenant controls..." : "No tenant settings found for this owner."}
                rows={(ownerWorkspace.tenantSettings || []).map((row) => (
                  <tr key={row.tenantId} className="border-t border-slate-100">
                    <td className="px-4 py-3">
                      <div className="font-medium text-slate-900">{row.companyName || row.tenantName}</div>
                      <div className="text-xs text-slate-500">{row.tenantSlug}</div>
                    </td>
                    <td className="px-4 py-3 text-slate-700">
                      <div>{row.planName || "-"}</div>
                      <div className="text-xs capitalize text-slate-500">{formatStatus(row.subscriptionStatus)}</div>
                    </td>
                    <td className="px-4 py-3">
                      <Badge className={row.ownerGroupSmsProviderRoute === "equence" ? "bg-blue-100 text-blue-700" : "bg-orange-100 text-orange-700"}>
                        {row.ownerGroupSmsProviderRoute === "equence" ? "Equence" : "Tata"}
                      </Badge>
                    </td>
                    <td className="px-4 py-3">
                      <Badge className={row.publicApiEnabled ? "bg-emerald-100 text-emerald-700" : "bg-slate-100 text-slate-600"}>
                        {row.publicApiEnabled ? "Enabled" : "Disabled"}
                      </Badge>
                    </td>
                    <td className="px-4 py-3">
                      <Badge className={row.smsGatewayReportEnabled ? "bg-emerald-100 text-emerald-700" : "bg-slate-100 text-slate-600"}>
                        {row.smsGatewayReportEnabled ? "Enabled" : "Disabled"}
                      </Badge>
                    </td>
                    <td className="px-4 py-3 text-slate-700">
                      {row.isTaxExempt ? "Tax exempt" : row.isReverseCharge ? "Reverse charge" : `${Number(row.taxRatePercent || 0)}% GST`}
                    </td>
                  </tr>
                ))}
              />
            </CardContent>
          </Card>

          <Card className="border-slate-200 shadow-sm">
            <CardHeader>
              <div className="flex items-start justify-between gap-4">
                <div>
                  <CardTitle className="text-xl">Owner Security Summary</CardTitle>
                  <CardDescription>Authentication and audit posture for the selected owner account.</CardDescription>
                </div>
                <Button variant="outline" className="rounded-xl" onClick={() => window.location.assign("/dashboard/platform-security-report")}>
                  Security report
                  <ArrowUpRight className="ml-2 h-4 w-4" />
                </Button>
              </div>
            </CardHeader>
            <CardContent className="space-y-5">
              <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
                <div className="rounded-2xl border border-slate-200 bg-slate-50/80 p-4">
                  <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">Logins</p>
                  <p className="mt-2 text-2xl font-bold text-slate-950">{ownerSecuritySummary.loginCount.toLocaleString()}</p>
                  <p className="text-sm text-slate-500">Tracked login rows for this owner</p>
                </div>
                <div className="rounded-2xl border border-slate-200 bg-slate-50/80 p-4">
                  <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">Active Sessions</p>
                  <p className="mt-2 text-2xl font-bold text-slate-950">{ownerSecuritySummary.activeSessions.toLocaleString()}</p>
                  <p className="text-sm text-slate-500">Non-revoked live sessions</p>
                </div>
                <div className="rounded-2xl border border-slate-200 bg-slate-50/80 p-4">
                  <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">Suspicious</p>
                  <p className="mt-2 text-2xl font-bold text-slate-950">{ownerSecuritySummary.suspiciousLogins.toLocaleString()}</p>
                  <p className="text-sm text-slate-500">Suspicious or blocked login posture</p>
                </div>
                <div className="rounded-2xl border border-slate-200 bg-slate-50/80 p-4">
                  <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">High Severity</p>
                  <p className="mt-2 text-2xl font-bold text-slate-950">{ownerSecuritySummary.highSeverity.toLocaleString()}</p>
                  <p className="text-sm text-slate-500">High-severity audit events</p>
                </div>
              </div>

              <SectionTable
                headers={["Time", "Action", "Tenant", "Severity", "IP / Device"]}
                empty={ownerWorkspaceLoading ? "Loading owner security activity..." : "No security activity found for this owner."}
                rows={(ownerWorkspace.security?.auditEvents || []).slice(0, 10).map((event) => (
                  <tr key={event.id} className="border-t border-slate-100">
                    <td className="px-4 py-3 text-slate-700">{formatDateTime(event.createdAtUtc)}</td>
                    <td className="px-4 py-3 font-medium text-slate-900">{event.action || "-"}</td>
                    <td className="px-4 py-3 text-slate-700">{event.tenantSlug || event.tenantId || "-"}</td>
                    <td className="px-4 py-3">
                      <Badge className={String(event.severity || "").toLowerCase() === "high" ? "bg-rose-100 text-rose-700" : String(event.severity || "").toLowerCase() === "medium" ? "bg-amber-100 text-amber-700" : "bg-slate-100 text-slate-700"}>
                        {formatStatus(event.severity || "low")}
                      </Badge>
                    </td>
                    <td className="px-4 py-3 text-slate-700">
                      <div>{event.ipAddress || "-"}</div>
                      <div className="text-xs text-slate-500">{event.deviceLabel || event.userAgent || "No device data"}</div>
                    </td>
                  </tr>
                ))}
              />
            </CardContent>
          </Card>
        </div>
      </div>

      <Card className="border-slate-200 shadow-sm">
        <CardHeader>
          <div className="flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
            <div>
              <CardTitle className="text-xl">Tenant Reporting Workspace</CardTitle>
              <CardDescription>Usage, subscriptions, invoices, members and audit activity for the selected tenant.</CardDescription>
            </div>
            <div className="w-full max-w-[220px]">
              <Label className="mb-2 block">Usage month</Label>
              <Input type="month" value={selectedMonth} onChange={(event) => setSelectedMonth(event.target.value)} className="h-11 rounded-xl" />
            </div>
          </div>
        </CardHeader>
        <CardContent>
          <div className="mb-5 grid gap-4 md:grid-cols-4">
            <div className="rounded-2xl border border-slate-200 bg-slate-50/80 p-4">
              <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">Tenant</p>
              <p className="mt-2 text-lg font-bold text-slate-950">{details?.tenant?.name || "No tenant selected"}</p>
              <p className="text-sm text-slate-500">{details?.tenant?.slug || "-"}</p>
            </div>
            <div className="rounded-2xl border border-slate-200 bg-slate-50/80 p-4">
              <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">Subscription</p>
              <p className="mt-2 text-lg font-bold text-slate-950">{details?.subscription?.plan?.name || "No Plan"}</p>
              <p className="text-sm text-slate-500 capitalize">{details?.subscription?.status || "inactive"}</p>
            </div>
            <div className="rounded-2xl border border-slate-200 bg-slate-50/80 p-4">
              <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">Members</p>
              <p className="mt-2 text-lg font-bold text-slate-950">{members.length.toLocaleString()}</p>
              <p className="text-sm text-slate-500">{subscriptions.length.toLocaleString()} subscription records</p>
            </div>
            <div className="rounded-2xl border border-slate-200 bg-slate-50/80 p-4">
              <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">Invoices</p>
              <p className="mt-2 text-lg font-bold text-slate-950">{invoices.length.toLocaleString()}</p>
              <p className="text-sm text-slate-500">{activity.length.toLocaleString()} audit events tracked</p>
            </div>
          </div>

          <Tabs defaultValue="usage" className="space-y-5">
            <TabsList className="flex h-auto w-full flex-wrap justify-start gap-2 rounded-2xl border border-slate-200 bg-slate-50 p-2">
              <TabsTrigger value="usage" className="rounded-xl">Usage</TabsTrigger>
              <TabsTrigger value="subscriptions" className="rounded-xl">Subscriptions</TabsTrigger>
              <TabsTrigger value="invoices" className="rounded-xl">Invoices</TabsTrigger>
              <TabsTrigger value="members" className="rounded-xl">Members</TabsTrigger>
              <TabsTrigger value="activity" className="rounded-xl">Activity</TabsTrigger>
            </TabsList>

            <TabsContent value="usage" className="space-y-4">
              <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
                {Object.entries(usage?.values || {}).map(([key, value]) => (
                  <div key={key} className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
                    <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">{key}</p>
                    <p className="mt-3 text-2xl font-bold text-slate-950">{Number(value || 0).toLocaleString()}</p>
                  </div>
                ))}
                {!Object.keys(usage?.values || {}).length ? (
                  <div className="rounded-2xl border border-dashed border-slate-300 bg-slate-50/80 p-6 text-sm text-slate-500 md:col-span-2 xl:col-span-4">
                    No usage has been recorded for the selected month.
                  </div>
                ) : null}
              </div>
            </TabsContent>

            <TabsContent value="subscriptions">
              <SectionTable
                headers={["Plan", "Status", "Cycle", "Started", "Renewal"]}
                empty="No subscription history for this tenant."
                rows={subscriptions.map((subscription) => (
                  <tr key={subscription.id} className="border-t border-slate-100">
                    <td className="px-4 py-3 font-medium text-slate-900">{subscription.plan?.name || "-"}</td>
                    <td className="px-4 py-3"><Badge variant="outline" className="capitalize">{subscription.status || "unknown"}</Badge></td>
                    <td className="px-4 py-3 text-slate-700">{subscription.billingCycle || "-"}</td>
                    <td className="px-4 py-3 text-slate-700">{formatDate(subscription.startedAtUtc)}</td>
                    <td className="px-4 py-3 text-slate-700">{formatDate(subscription.renewAtUtc)}</td>
                  </tr>
                ))}
              />
            </TabsContent>

            <TabsContent value="invoices">
              <SectionTable
                headers={["Invoice", "Date", "Amount", "Status"]}
                empty="No invoices found for this tenant."
                rows={invoices.map((invoice) => (
                  <tr key={invoice.id} className="border-t border-slate-100">
                    <td className="px-4 py-3 font-medium text-slate-900">{invoice.invoiceNo || invoice.id}</td>
                    <td className="px-4 py-3 text-slate-700">{formatDate(invoice.createdAtUtc)}</td>
                    <td className="px-4 py-3 text-slate-900">{formatMoney(invoice.total || 0)}</td>
                    <td className="px-4 py-3"><Badge variant="outline" className="capitalize">{invoice.status || "unknown"}</Badge></td>
                  </tr>
                ))}
              />
            </TabsContent>

            <TabsContent value="members">
              <SectionTable
                headers={["Name", "Email", "Role", "Status"]}
                empty="No members found for this tenant."
                rows={members.map((member) => (
                  <tr key={member.userId} className="border-t border-slate-100">
                    <td className="px-4 py-3 font-medium text-slate-900">{member.name || "-"}</td>
                    <td className="px-4 py-3 text-slate-700">{member.email || "-"}</td>
                    <td className="px-4 py-3 text-slate-700">{member.role || "-"}</td>
                    <td className="px-4 py-3"><Badge variant="outline">{member.isActive ? "active" : "inactive"}</Badge></td>
                  </tr>
                ))}
              />
            </TabsContent>

            <TabsContent value="activity">
              <SectionTable
                headers={["Time", "Action", "Details", "IP / Device"]}
                empty="No activity has been recorded for this tenant."
                rows={activity.map((event) => (
                  <tr key={event.id} className="border-t border-slate-100">
                    <td className="px-4 py-3 text-slate-700">{formatDateTime(event.createdAtUtc)}</td>
                    <td className="px-4 py-3 font-medium text-slate-900">{event.action || "-"}</td>
                    <td className="px-4 py-3 text-slate-700">{event.details || "-"}</td>
                    <td className="px-4 py-3 text-slate-700">
                      <div>{event.ipAddress || "-"}</div>
                      <div className="text-xs text-slate-500">{event.deviceLabel || event.userAgent || "No device data"}</div>
                    </td>
                  </tr>
                ))}
              />
            </TabsContent>
          </Tabs>
        </CardContent>
      </Card>

      <ApiDocsViewer
        open={docViewer.open}
        onOpenChange={(open) => setDocViewer((prev) => ({ ...prev, open }))}
        type={docViewer.type}
        onTypeChange={(nextType) => setDocViewer({ open: true, type: nextType })}
      />
    </div>
  );
}
