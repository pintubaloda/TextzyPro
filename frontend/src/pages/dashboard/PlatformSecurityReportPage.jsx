import { useEffect, useMemo, useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Textarea } from "@/components/ui/textarea";
import { AlertTriangle, Ban, Download, Filter, History, Laptop2, RefreshCcw, ShieldCheck, ShieldPlus, Trash2, UserCircle2 } from "lucide-react";
import { toast } from "sonner";
import {
  blockPlatformSessionIp,
  createPlatformSecurityIpRule,
  deletePlatformSecurityIpRule,
  exportPlatformSecurityReport,
  getPlatformCustomers,
  getPlatformSecurityReport,
  getPlatformUsers,
  revokePlatformSession,
} from "@/lib/api";

const fmt = (value) => {
  if (!value) return "-";
  try {
    return new Date(value).toLocaleString();
  } catch {
    return String(value);
  }
};

const todayRange = () => {
  const now = new Date();
  const start = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  return {
    fromUtc: start.toISOString().slice(0, 16),
    toUtc: now.toISOString().slice(0, 16),
  };
};

const daysRange = (days) => {
  const now = new Date();
  const start = new Date(now);
  start.setDate(now.getDate() - days);
  return {
    fromUtc: start.toISOString().slice(0, 16),
    toUtc: now.toISOString().slice(0, 16),
  };
};

function KpiCard({ title, value, hint, icon: Icon, tone = "slate" }) {
  const tones = {
    slate: "border-slate-200 bg-slate-50 text-slate-700",
    orange: "border-orange-200 bg-orange-50 text-orange-700",
    emerald: "border-emerald-200 bg-emerald-50 text-emerald-700",
    blue: "border-blue-200 bg-blue-50 text-blue-700",
    rose: "border-rose-200 bg-rose-50 text-rose-700",
  };

  return (
    <Card className="border-slate-200 shadow-sm">
      <CardContent className="pt-5">
        <div className="flex items-start justify-between gap-4">
          <div>
            <p className="text-xs font-semibold uppercase tracking-[0.16em] text-slate-500">{title}</p>
            <p className="mt-2 text-3xl font-bold text-slate-950">{Number(value || 0).toLocaleString()}</p>
            <p className="mt-1 text-sm text-slate-500">{hint}</p>
          </div>
          <div className={`inline-flex h-11 w-11 items-center justify-center rounded-2xl border ${tones[tone] || tones.slate}`}>
            <Icon className="h-5 w-5" />
          </div>
        </div>
      </CardContent>
    </Card>
  );
}

function TableShell({ headers, children, empty, colSpan }) {
  const rows = Array.isArray(children) ? children : [];
  return (
    <div className="overflow-hidden rounded-2xl border border-slate-200 bg-white">
      <div className="overflow-x-auto">
        <table className="w-full min-w-[1080px] text-sm">
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
            {rows}
            {!rows.length ? (
              <tr>
                <td colSpan={colSpan || headers.length} className="px-4 py-10 text-center text-slate-500">
                  {empty}
                </td>
              </tr>
            ) : null}
          </tbody>
        </table>
      </div>
    </div>
  );
}

function SeverityBadge({ severity }) {
  const normalized = String(severity || "low").toLowerCase();
  const classes =
    normalized === "high"
      ? "bg-rose-100 text-rose-700"
      : normalized === "medium"
      ? "bg-amber-100 text-amber-700"
      : "bg-slate-100 text-slate-700";
  return <Badge className={classes}>{normalized}</Badge>;
}

function SessionStateBadge({ row }) {
  const isRevoked = !!row.revokedAtUtc;
  const isExpired = !isRevoked && row.expiresAtUtc && new Date(row.expiresAtUtc) <= new Date();
  const stateText = isRevoked ? "Revoked" : isExpired ? "Expired" : "Active";
  const classes = isRevoked ? "bg-rose-100 text-rose-700" : isExpired ? "bg-amber-100 text-amber-700" : "bg-emerald-100 text-emerald-700";
  return <Badge className={classes}>{stateText}</Badge>;
}

function IpPolicyBadge({ value }) {
  const normalized = String(value || "open").toLowerCase();
  const classes =
    normalized === "blocked"
      ? "bg-rose-100 text-rose-700"
      : normalized === "allowlisted"
      ? "bg-emerald-100 text-emerald-700"
      : normalized === "not_allowlisted"
      ? "bg-amber-100 text-amber-700"
      : "bg-slate-100 text-slate-700";
  const label =
    normalized === "allowlisted"
      ? "Allowlisted"
      : normalized === "not_allowlisted"
      ? "Missing allowlist"
      : normalized === "blocked"
      ? "Blocked"
      : "Open";
  return <Badge className={classes}>{label}</Badge>;
}

export default function PlatformSecurityReportPage() {
  const [tenants, setTenants] = useState([]);
  const [users, setUsers] = useState([]);
  const [loading, setLoading] = useState(true);
  const [exporting, setExporting] = useState(false);
  const [revokingSessionId, setRevokingSessionId] = useState("");
  const [blockingSessionId, setBlockingSessionId] = useState("");
  const [savingIpRule, setSavingIpRule] = useState(false);
  const [deletingIpRuleId, setDeletingIpRuleId] = useState("");
  const [report, setReport] = useState({ summary: {}, ipRules: [], loginHistory: [], sessionsByUser: [], auditEvents: [], notes: {} });
  const [selectedUser, setSelectedUser] = useState(null);
  const [ipRuleForm, setIpRuleForm] = useState({
    tenantId: "all",
    ruleType: "block",
    ipRule: "",
    note: "",
  });
  const [filters, setFilters] = useState({
    tenantId: "",
    userId: "",
    actionContains: "",
    sessionStatus: "all",
    fromUtc: "",
    toUtc: "",
    limit: 200,
  });

  const load = async (activeFilters = filters) => {
    try {
      setLoading(true);
      const [tenantRows, userRows, reportRows] = await Promise.all([
        getPlatformCustomers("").catch(() => []),
        getPlatformUsers("").catch(() => []),
        getPlatformSecurityReport(activeFilters),
      ]);
      setTenants(Array.isArray(tenantRows) ? tenantRows : []);
      setUsers(Array.isArray(userRows) ? userRows : []);
      setReport(reportRows || { summary: {}, ipRules: [], loginHistory: [], sessionsByUser: [], auditEvents: [], notes: {} });
    } catch (error) {
      toast.error(error?.message || "Failed to load security report");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const applyFilters = async (nextFilters = filters) => {
    await load(nextFilters);
  };

  const applyPreset = async (preset) => {
    const patch =
      preset === "today" ? todayRange() :
      preset === "7d" ? daysRange(7) :
      preset === "30d" ? daysRange(30) :
      { fromUtc: "", toUtc: "" };
    const next = { ...filters, ...patch };
    setFilters(next);
    await applyFilters(next);
  };

  const downloadCsv = async () => {
    try {
      setExporting(true);
      const blob = await exportPlatformSecurityReport(filters);
      const url = window.URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = `platform-security-report-${new Date().toISOString().slice(0, 19).replace(/[:T]/g, "-")}.csv`;
      document.body.appendChild(a);
      a.click();
      a.remove();
      window.URL.revokeObjectURL(url);
    } catch (error) {
      toast.error(error?.message || "Failed to export security report");
    } finally {
      setExporting(false);
    }
  };

  const onRevokeSession = async (sessionId) => {
    try {
      setRevokingSessionId(sessionId);
      await revokePlatformSession(sessionId);
      toast.success("Session revoked.");
      await applyFilters();
    } catch (error) {
      toast.error(error?.message || "Failed to revoke session");
    } finally {
      setRevokingSessionId("");
    }
  };

  const onCreateIpRule = async (payload) => {
    try {
      setSavingIpRule(true);
      await createPlatformSecurityIpRule(payload);
      toast.success(payload.ruleType === "allow" ? "IP added to allowlist." : "IP added to blocklist.");
      setIpRuleForm((prev) => ({ ...prev, ipRule: "", note: "" }));
      await applyFilters();
    } catch (error) {
      toast.error(error?.message || "Failed to save IP rule");
    } finally {
      setSavingIpRule(false);
    }
  };

  const onDeleteIpRule = async (ruleId) => {
    try {
      setDeletingIpRuleId(ruleId);
      await deletePlatformSecurityIpRule(ruleId);
      toast.success("IP rule removed.");
      await applyFilters();
    } catch (error) {
      toast.error(error?.message || "Failed to remove IP rule");
    } finally {
      setDeletingIpRuleId("");
    }
  };

  const onBlockSessionIp = async (sessionId) => {
    try {
      setBlockingSessionId(sessionId);
      const result = await blockPlatformSessionIp(sessionId);
      toast.success(`IP blocked.${Number(result?.sessionsRevoked || 0) > 0 ? ` ${result.sessionsRevoked} session(s) revoked.` : ""}`);
      await applyFilters();
    } catch (error) {
      toast.error(error?.message || "Failed to block session IP");
    } finally {
      setBlockingSessionId("");
    }
  };

  const summary = report?.summary || {};
  const ipRules = useMemo(() => (Array.isArray(report?.ipRules) ? report.ipRules : []), [report]);
  const loginHistory = useMemo(() => (Array.isArray(report?.loginHistory) ? report.loginHistory : []), [report]);
  const sessionsByUser = useMemo(() => (Array.isArray(report?.sessionsByUser) ? report.sessionsByUser : []), [report]);
  const auditEvents = useMemo(() => (Array.isArray(report?.auditEvents) ? report.auditEvents : []), [report]);
  const note = report?.notes?.location || "IP address and device metadata are captured. Lat/long is not collected.";
  const ipPolicyNote = report?.notes?.ipPolicy || "Block rules win over allow rules.";

  const tenantOptions = useMemo(() => (Array.isArray(tenants) ? tenants : []), [tenants]);
  const userOptions = useMemo(() => (Array.isArray(users) ? users : []), [users]);

  const drilldownSessions = useMemo(() => {
    if (!selectedUser?.userId) return [];
    return loginHistory.filter((row) => row.userId === selectedUser.userId);
  }, [loginHistory, selectedUser]);

  const drilldownAudit = useMemo(() => {
    if (!selectedUser?.userId) return [];
    return auditEvents.filter((row) => row.actorUserId === selectedUser.userId);
  }, [auditEvents, selectedUser]);

  return (
    <div className="space-y-6" data-testid="platform-security-report-page">
      <section className="rounded-[28px] border border-slate-200 bg-gradient-to-br from-slate-950 via-slate-900 to-orange-950 p-6 text-white shadow-sm">
        <div className="flex flex-col gap-6 xl:flex-row xl:items-end xl:justify-between">
          <div className="max-w-3xl">
            <Badge className="border border-white/15 bg-white/10 text-white hover:bg-white/10">Platform Security Report</Badge>
            <h1 className="mt-4 text-3xl font-bold tracking-tight">Login, session, and audit visibility for the full SaaS platform</h1>
            <p className="mt-3 max-w-2xl text-sm text-slate-200">
              Review login history, per-user session footprint, suspicious patterns, and action-level audit trails across all tenants.
            </p>
          </div>
          <div className="flex flex-wrap gap-3">
            <Button variant="outline" className="border-white/20 bg-white/5 text-white hover:bg-white/10" onClick={() => applyFilters()} disabled={loading}>
              <RefreshCcw className={`mr-2 h-4 w-4 ${loading ? "animate-spin" : ""}`} />
              Refresh
            </Button>
            <Button className="bg-orange-500 text-white hover:bg-orange-600" onClick={downloadCsv} disabled={exporting}>
              <Download className="mr-2 h-4 w-4" />
              {exporting ? "Exporting..." : "Export CSV"}
            </Button>
          </div>
        </div>
      </section>

      <div className="grid gap-4 lg:grid-cols-5">
        <KpiCard title="Logins" value={summary.loginCount} hint="Recent login/session records after filters" icon={History} tone="orange" />
        <KpiCard title="Active Sessions" value={summary.activeSessions} hint="Currently valid web sessions" icon={Laptop2} tone="emerald" />
        <KpiCard title="Unique Users" value={summary.uniqueUsers} hint="Distinct users represented in session history" icon={UserCircle2} tone="blue" />
        <KpiCard title="Audit Events" value={summary.auditEvents} hint="Action-level audit rows after filters" icon={ShieldCheck} tone="slate" />
        <KpiCard title="Suspicious" value={summary.suspiciousLogins} hint="Logins flagged by heuristics" icon={AlertTriangle} tone="rose" />
      </div>

      <Card className="border-slate-200 shadow-sm">
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <ShieldPlus className="h-5 w-5 text-orange-500" />
            IP Access Controls
          </CardTitle>
          <CardDescription>{ipPolicyNote}</CardDescription>
        </CardHeader>
        <CardContent className="space-y-5">
          <div className="grid gap-4 xl:grid-cols-[1.3fr_1fr]">
            <div className="rounded-2xl border border-slate-200 bg-slate-50 p-4">
              <div className="grid gap-4 md:grid-cols-2">
                <div className="space-y-2">
                  <Label>Rule Type</Label>
                  <Select value={ipRuleForm.ruleType} onValueChange={(value) => setIpRuleForm((prev) => ({ ...prev, ruleType: value }))}>
                    <SelectTrigger><SelectValue /></SelectTrigger>
                    <SelectContent>
                      <SelectItem value="allow">Whitelist</SelectItem>
                      <SelectItem value="block">Blocklist</SelectItem>
                    </SelectContent>
                  </Select>
                </div>
                <div className="space-y-2">
                  <Label>Tenant Scope</Label>
                  <Select value={ipRuleForm.tenantId} onValueChange={(value) => setIpRuleForm((prev) => ({ ...prev, tenantId: value }))}>
                    <SelectTrigger><SelectValue /></SelectTrigger>
                    <SelectContent>
                      <SelectItem value="all">All tenants</SelectItem>
                      {tenantOptions.map((tenant) => (
                        <SelectItem key={tenant.tenantId} value={tenant.tenantId}>
                          {tenant.tenantName || tenant.companyName || tenant.tenantSlug}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>
                <div className="space-y-2 md:col-span-2">
                  <Label>IP or CIDR</Label>
                  <Input
                    value={ipRuleForm.ipRule}
                    onChange={(e) => setIpRuleForm((prev) => ({ ...prev, ipRule: e.target.value }))}
                    placeholder="203.0.113.10 or 198.51.100.0/24"
                  />
                </div>
                <div className="space-y-2 md:col-span-2">
                  <Label>Note</Label>
                  <Textarea
                    value={ipRuleForm.note}
                    onChange={(e) => setIpRuleForm((prev) => ({ ...prev, note: e.target.value }))}
                    placeholder="Reason for this rule"
                    rows={3}
                  />
                </div>
              </div>
              <div className="mt-4 flex flex-wrap gap-3">
                <Button
                  className="bg-orange-500 text-white hover:bg-orange-600"
                  disabled={savingIpRule || !ipRuleForm.ipRule.trim()}
                  onClick={() => onCreateIpRule({
                    tenantId: ipRuleForm.tenantId,
                    ruleType: ipRuleForm.ruleType,
                    ipRule: ipRuleForm.ipRule.trim(),
                    note: ipRuleForm.note.trim(),
                    revokeMatchingSessions: ipRuleForm.ruleType === "block",
                  })}
                >
                  {savingIpRule ? "Saving..." : ipRuleForm.ruleType === "allow" ? "Add to whitelist" : "Add to blocklist"}
                </Button>
                <p className="self-center text-xs text-slate-500">
                  Use exact IPs or CIDR ranges. Removing a rule immediately restores normal access evaluation.
                </p>
              </div>
            </div>

            <div className="rounded-2xl border border-slate-200">
              <div className="border-b border-slate-200 px-4 py-3">
                <p className="text-sm font-semibold text-slate-900">Active rules</p>
                <p className="text-xs text-slate-500">{ipRules.length} session access rule(s)</p>
              </div>
              <div className="max-h-[320px] overflow-auto">
                {ipRules.length ? ipRules.map((rule) => (
                  <div key={rule.id} className="border-b border-slate-100 px-4 py-3 last:border-b-0">
                    <div className="flex flex-wrap items-start justify-between gap-3">
                      <div className="space-y-1">
                        <div className="flex flex-wrap items-center gap-2">
                          <Badge className={String(rule.ruleType).toLowerCase() === "block" ? "bg-rose-100 text-rose-700" : "bg-emerald-100 text-emerald-700"}>
                            {String(rule.ruleType).toLowerCase() === "block" ? "Blocklist" : "Whitelist"}
                          </Badge>
                          <span className="font-medium text-slate-900">{rule.ipRule}</span>
                        </div>
                        <p className="text-xs text-slate-500">
                          {rule.tenantName || "All tenants"}{rule.tenantSlug ? ` (${rule.tenantSlug})` : ""}
                        </p>
                        <p className="text-xs text-slate-600">{rule.note || "No note"}</p>
                        <p className="text-[11px] text-slate-400">Created {fmt(rule.createdAtUtc)}</p>
                      </div>
                      <Button
                        size="sm"
                        variant="outline"
                        className="border-rose-200 text-rose-700 hover:bg-rose-50"
                        disabled={deletingIpRuleId === rule.id}
                        onClick={() => onDeleteIpRule(rule.id)}
                      >
                        <Trash2 className="mr-2 h-4 w-4" />
                        {deletingIpRuleId === rule.id ? "Removing..." : "Remove"}
                      </Button>
                    </div>
                  </div>
                )) : (
                  <div className="px-4 py-10 text-center text-sm text-slate-500">
                    No active session IP rules. Add a whitelist or blocklist entry to control login access.
                  </div>
                )}
              </div>
            </div>
          </div>
        </CardContent>
      </Card>

      <Card className="border-slate-200 shadow-sm">
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <Filter className="h-5 w-5 text-orange-500" />
            Filters
          </CardTitle>
          <CardDescription>{note}</CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="flex flex-wrap gap-2">
            <Button variant="outline" size="sm" onClick={() => applyPreset("today")}>Today</Button>
            <Button variant="outline" size="sm" onClick={() => applyPreset("7d")}>7d</Button>
            <Button variant="outline" size="sm" onClick={() => applyPreset("30d")}>30d</Button>
            <Button variant="outline" size="sm" onClick={() => applyPreset("clear")}>Clear Dates</Button>
          </div>
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-6">
            <div className="space-y-2">
              <Label>Tenant</Label>
              <Select value={filters.tenantId || "all"} onValueChange={(value) => setFilters((prev) => ({ ...prev, tenantId: value === "all" ? "" : value }))}>
                <SelectTrigger><SelectValue placeholder="All tenants" /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">All tenants</SelectItem>
                  {tenantOptions.map((tenant) => (
                    <SelectItem key={tenant.tenantId} value={tenant.tenantId}>
                      {tenant.tenantName || tenant.companyName || tenant.tenantSlug}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>

            <div className="space-y-2">
              <Label>User</Label>
              <Select value={filters.userId || "all"} onValueChange={(value) => setFilters((prev) => ({ ...prev, userId: value === "all" ? "" : value }))}>
                <SelectTrigger><SelectValue placeholder="All users" /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">All users</SelectItem>
                  {userOptions.map((user) => (
                    <SelectItem key={user.userId} value={user.userId}>
                      {user.email}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>

            <div className="space-y-2">
              <Label>Session Status</Label>
              <Select value={filters.sessionStatus} onValueChange={(value) => setFilters((prev) => ({ ...prev, sessionStatus: value }))}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">All</SelectItem>
                  <SelectItem value="active">Active</SelectItem>
                  <SelectItem value="revoked">Revoked</SelectItem>
                  <SelectItem value="expired">Expired</SelectItem>
                </SelectContent>
              </Select>
            </div>

            <div className="space-y-2">
              <Label>Action Filter</Label>
              <Input value={filters.actionContains} onChange={(e) => setFilters((prev) => ({ ...prev, actionContains: e.target.value }))} placeholder="team.invite, billing..." />
            </div>

            <div className="space-y-2">
              <Label>From (UTC)</Label>
              <Input type="datetime-local" value={filters.fromUtc} onChange={(e) => setFilters((prev) => ({ ...prev, fromUtc: e.target.value }))} />
            </div>

            <div className="space-y-2">
              <Label>To (UTC)</Label>
              <Input type="datetime-local" value={filters.toUtc} onChange={(e) => setFilters((prev) => ({ ...prev, toUtc: e.target.value }))} />
            </div>

            <div className="space-y-2">
              <Label>Row Limit</Label>
              <Input type="number" min={20} max={1000} value={filters.limit} onChange={(e) => setFilters((prev) => ({ ...prev, limit: Number(e.target.value || 200) }))} />
            </div>
          </div>
        </CardContent>
      </Card>

      <Tabs defaultValue="logins" className="space-y-4">
        <TabsList className="grid w-full max-w-2xl grid-cols-3 rounded-2xl bg-slate-100">
          <TabsTrigger value="logins">Login History</TabsTrigger>
          <TabsTrigger value="sessions">Per-User Sessions</TabsTrigger>
          <TabsTrigger value="audit">Audit Trail</TabsTrigger>
        </TabsList>

        <TabsContent value="logins">
          <Card className="border-slate-200 shadow-sm">
            <CardHeader>
              <CardTitle>Login history</CardTitle>
              <CardDescription>Each row represents a created web session with IP, device, and 2FA markers.</CardDescription>
            </CardHeader>
            <CardContent>
              <TableShell
                headers={["User", "Tenant", "State", "Risk", "IP Policy", "Device / IP", "Created", "Last Seen", "2FA", "Actions"]}
                empty={loading ? "Loading login history..." : "No login history found for the current filters."}
              >
                {loginHistory.map((row) => (
                  <tr key={row.sessionId} className={`border-t border-slate-100 ${row.isSuspicious ? "bg-rose-50/50" : ""}`}>
                    <td className="px-4 py-3">
                      <button type="button" className="text-left" onClick={() => setSelectedUser({ userId: row.userId, userName: row.userName, userEmail: row.userEmail })}>
                        <p className="font-medium text-slate-900">{row.userName || "-"}</p>
                        <p className="text-xs text-slate-500">{row.userEmail}</p>
                      </button>
                    </td>
                    <td className="px-4 py-3">
                      <p className="font-medium text-slate-900">{row.tenantName}</p>
                      <p className="text-xs text-slate-500">{row.tenantSlug}</p>
                    </td>
                    <td className="px-4 py-3">
                      <SessionStateBadge row={row} />
                      <p className="mt-2 text-xs text-slate-500">{row.role || "-"}</p>
                    </td>
                    <td className="px-4 py-3">
                      {row.isSuspicious ? (
                        <div className="space-y-2">
                          <Badge className="bg-rose-100 text-rose-700">Suspicious</Badge>
                          <p className="max-w-[220px] text-xs text-rose-700">{row.suspiciousReasons || "-"}</p>
                        </div>
                      ) : (
                        <Badge className="bg-emerald-100 text-emerald-700">Normal</Badge>
                      )}
                    </td>
                    <td className="px-4 py-3">
                      <div className="space-y-2">
                        <IpPolicyBadge value={row.ipPolicyStatus} />
                        <p className="max-w-[180px] text-xs text-slate-500">{row.ipPolicyRule || "No matching rule"}</p>
                      </div>
                    </td>
                    <td className="px-4 py-3">
                      <p className="font-medium text-slate-900">{row.deviceLabel || row.userAgent || "-"}</p>
                      <p className="text-xs text-slate-500">Created: {row.ipAddress || "-"}</p>
                      <p className="text-xs text-slate-500">Last seen: {row.lastSeenIpAddress || "-"}</p>
                    </td>
                    <td className="px-4 py-3 text-slate-700">{fmt(row.createdAtUtc)}</td>
                    <td className="px-4 py-3 text-slate-700">{fmt(row.lastSeenAtUtc)}</td>
                    <td className="px-4 py-3">
                      <div className="space-y-1 text-xs">
                        <p className={row.twoFactorVerifiedAtUtc ? "text-emerald-700" : "text-slate-500"}>
                          {row.twoFactorVerifiedAtUtc ? `2FA verified ${fmt(row.twoFactorVerifiedAtUtc)}` : "2FA not verified"}
                        </p>
                        <p className={row.stepUpVerifiedAtUtc ? "text-blue-700" : "text-slate-500"}>
                          {row.stepUpVerifiedAtUtc ? `Step-up ${fmt(row.stepUpVerifiedAtUtc)}` : "No recent step-up"}
                        </p>
                      </div>
                    </td>
                    <td className="px-4 py-3">
                      <div className="flex flex-col gap-2">
                        <Button size="sm" variant="outline" onClick={() => setSelectedUser({ userId: row.userId, userName: row.userName, userEmail: row.userEmail })}>
                          View user
                        </Button>
                        <Button
                          size="sm"
                          variant="outline"
                          disabled={!String(row.lastSeenIpAddress || row.ipAddress || "").trim() || savingIpRule}
                          onClick={() => onCreateIpRule({
                            tenantId: row.tenantId,
                            ruleType: "allow",
                            ipRule: String(row.lastSeenIpAddress || row.ipAddress || "").trim(),
                            note: `Allowlisted from session ${row.sessionId}`,
                            revokeMatchingSessions: false,
                          })}
                        >
                          <ShieldCheck className="mr-2 h-4 w-4" />
                          Allow IP
                        </Button>
                        <Button
                          size="sm"
                          variant="outline"
                          className="border-amber-200 text-amber-700 hover:bg-amber-50"
                          disabled={!String(row.lastSeenIpAddress || row.ipAddress || "").trim() || blockingSessionId === row.sessionId}
                          onClick={() => onBlockSessionIp(row.sessionId)}
                        >
                          <Ban className="mr-2 h-4 w-4" />
                          {blockingSessionId === row.sessionId ? "Blocking..." : "Block IP"}
                        </Button>
                        {!row.revokedAtUtc ? (
                          <Button
                            size="sm"
                            className="bg-rose-600 text-white hover:bg-rose-700"
                            disabled={revokingSessionId === row.sessionId}
                            onClick={() => onRevokeSession(row.sessionId)}
                          >
                            {revokingSessionId === row.sessionId ? "Revoking..." : "Revoke"}
                          </Button>
                        ) : null}
                      </div>
                    </td>
                  </tr>
                ))}
              </TableShell>
            </CardContent>
          </Card>
        </TabsContent>

        <TabsContent value="sessions">
          <Card className="border-slate-200 shadow-sm">
            <CardHeader>
              <CardTitle>Per-user session history</CardTitle>
              <CardDescription>Quick aggregation of session footprint per user across the platform.</CardDescription>
            </CardHeader>
            <CardContent>
              <TableShell
                headers={["User", "Sessions", "Active", "Last Tenant", "Last Device", "Last IP", "Last Seen", "Actions"]}
                empty={loading ? "Loading session summary..." : "No session summary available for the current filters."}
              >
                {sessionsByUser.map((row) => (
                  <tr key={row.userId} className="border-t border-slate-100">
                    <td className="px-4 py-3">
                      <p className="font-medium text-slate-900">{row.userName || "-"}</p>
                      <p className="text-xs text-slate-500">{row.userEmail}</p>
                    </td>
                    <td className="px-4 py-3 font-medium text-slate-900">{Number(row.sessionCount || 0).toLocaleString()}</td>
                    <td className="px-4 py-3">
                      <Badge className="bg-emerald-100 text-emerald-700">{Number(row.activeSessions || 0).toLocaleString()}</Badge>
                    </td>
                    <td className="px-4 py-3 text-slate-700">{row.lastTenantName || "-"}</td>
                    <td className="px-4 py-3 text-slate-700">{row.lastDeviceLabel || "-"}</td>
                    <td className="px-4 py-3 text-slate-700">{row.lastIpAddress || "-"}</td>
                    <td className="px-4 py-3 text-slate-700">{fmt(row.lastSeenAtUtc)}</td>
                    <td className="px-4 py-3">
                      <Button size="sm" variant="outline" onClick={() => setSelectedUser({ userId: row.userId, userName: row.userName, userEmail: row.userEmail })}>
                        View detail
                      </Button>
                    </td>
                  </tr>
                ))}
              </TableShell>
            </CardContent>
          </Card>
        </TabsContent>

        <TabsContent value="audit">
          <Card className="border-slate-200 shadow-sm">
            <CardHeader>
              <CardTitle>Action audit trail</CardTitle>
              <CardDescription>Filterable action history with actor, tenant, IP address, severity, and device metadata.</CardDescription>
            </CardHeader>
            <CardContent>
              <TableShell
                headers={["Severity", "Action", "Actor", "Tenant", "IP / Device", "Details", "When"]}
                empty={loading ? "Loading audit trail..." : "No audit events found for the current filters."}
              >
                {auditEvents.map((row) => (
                  <tr key={row.id} className="border-t border-slate-100 align-top">
                    <td className="px-4 py-3">
                      <SeverityBadge severity={row.severity} />
                    </td>
                    <td className="px-4 py-3">
                      <Badge variant="outline" className="border-slate-300 text-slate-700">{row.action}</Badge>
                    </td>
                    <td className="px-4 py-3">
                      <button type="button" className="text-left" onClick={() => row.actorUserId && setSelectedUser({ userId: row.actorUserId, userName: row.actorName, userEmail: row.actorEmail })}>
                        <p className="font-medium text-slate-900">{row.actorName || "-"}</p>
                        <p className="text-xs text-slate-500">{row.actorEmail || "-"}</p>
                      </button>
                    </td>
                    <td className="px-4 py-3">
                      <p className="font-medium text-slate-900">{row.tenantName || "Platform"}</p>
                      <p className="text-xs text-slate-500">{row.tenantSlug || "-"}</p>
                    </td>
                    <td className="px-4 py-3">
                      <p className="font-medium text-slate-900">{row.ipAddress || "-"}</p>
                      <p className="text-xs text-slate-500">{row.deviceLabel || row.userAgent || "-"}</p>
                    </td>
                    <td className="max-w-[360px] break-words px-4 py-3 text-slate-700">{row.details || "-"}</td>
                    <td className="whitespace-nowrap px-4 py-3 text-slate-700">{fmt(row.createdAtUtc)}</td>
                  </tr>
                ))}
              </TableShell>
            </CardContent>
          </Card>
        </TabsContent>
      </Tabs>

      <Dialog open={!!selectedUser} onOpenChange={(open) => !open && setSelectedUser(null)}>
        <DialogContent className="max-w-5xl border-slate-200">
          <DialogHeader>
            <DialogTitle>{selectedUser?.userName || selectedUser?.userEmail || "User detail"}</DialogTitle>
            <DialogDescription>
              Session and audit drill-down for the selected user within the current filters.
            </DialogDescription>
          </DialogHeader>
          <div className="grid gap-4 lg:grid-cols-3">
            <Card className="border-slate-200 lg:col-span-1">
              <CardHeader>
                <CardTitle className="text-base">User summary</CardTitle>
              </CardHeader>
              <CardContent className="space-y-3 text-sm text-slate-700">
                <div>
                  <div className="text-xs uppercase tracking-[0.16em] text-slate-500">Email</div>
                  <div className="mt-1 font-medium text-slate-900">{selectedUser?.userEmail || "-"}</div>
                </div>
                <div>
                  <div className="text-xs uppercase tracking-[0.16em] text-slate-500">Sessions in report</div>
                  <div className="mt-1 font-medium text-slate-900">{drilldownSessions.length}</div>
                </div>
                <div>
                  <div className="text-xs uppercase tracking-[0.16em] text-slate-500">Audit events in report</div>
                  <div className="mt-1 font-medium text-slate-900">{drilldownAudit.length}</div>
                </div>
                <div>
                  <div className="text-xs uppercase tracking-[0.16em] text-slate-500">Distinct IPs</div>
                  <div className="mt-1 font-medium text-slate-900">
                    {new Set(drilldownSessions.map((x) => x.lastSeenIpAddress || x.ipAddress).filter(Boolean)).size}
                  </div>
                </div>
              </CardContent>
            </Card>

            <Card className="border-slate-200 lg:col-span-2">
              <CardHeader>
                <CardTitle className="text-base">Recent sessions</CardTitle>
              </CardHeader>
              <CardContent className="space-y-3">
                {drilldownSessions.length ? drilldownSessions.map((row) => (
                  <div key={row.sessionId} className="rounded-2xl border border-slate-200 p-4">
                    <div className="flex flex-wrap items-start justify-between gap-3">
                      <div className="space-y-1">
                        <div className="flex flex-wrap items-center gap-2">
                          <SessionStateBadge row={row} />
                          {row.isSuspicious ? <Badge className="bg-rose-100 text-rose-700">Suspicious</Badge> : null}
                        </div>
                        <p className="text-sm font-medium text-slate-900">{row.tenantName} <span className="text-xs text-slate-500">({row.tenantSlug})</span></p>
                        <p className="text-xs text-slate-500">{row.deviceLabel || row.userAgent || "-"}</p>
                      </div>
                      {!row.revokedAtUtc ? (
                        <Button
                          size="sm"
                          className="bg-rose-600 text-white hover:bg-rose-700"
                          disabled={revokingSessionId === row.sessionId}
                          onClick={() => onRevokeSession(row.sessionId)}
                        >
                          {revokingSessionId === row.sessionId ? "Revoking..." : "Revoke"}
                        </Button>
                      ) : null}
                    </div>
                    <div className="mt-3 grid gap-2 text-xs text-slate-600 md:grid-cols-2">
                      <div>Created: {fmt(row.createdAtUtc)}</div>
                      <div>Last Seen: {fmt(row.lastSeenAtUtc)}</div>
                      <div>Created IP: {row.ipAddress || "-"}</div>
                      <div>Last Seen IP: {row.lastSeenIpAddress || "-"}</div>
                      <div>2FA: {row.twoFactorVerifiedAtUtc ? fmt(row.twoFactorVerifiedAtUtc) : "Not verified"}</div>
                      <div>Step-up: {row.stepUpVerifiedAtUtc ? fmt(row.stepUpVerifiedAtUtc) : "Not verified"}</div>
                    </div>
                    {row.isSuspicious ? <p className="mt-3 text-xs text-rose-700">{row.suspiciousReasons || "-"}</p> : null}
                  </div>
                )) : <div className="rounded-2xl border border-dashed border-slate-200 px-4 py-8 text-center text-sm text-slate-500">No sessions found for this user under the current filters.</div>}
              </CardContent>
            </Card>
          </div>

          <Card className="border-slate-200">
            <CardHeader>
              <CardTitle className="text-base">Recent actions</CardTitle>
            </CardHeader>
            <CardContent className="space-y-3">
              {drilldownAudit.length ? drilldownAudit.slice(0, 20).map((row) => (
                <div key={row.id} className="rounded-2xl border border-slate-200 p-4">
                  <div className="flex flex-wrap items-center gap-2">
                    <SeverityBadge severity={row.severity} />
                    <Badge variant="outline" className="border-slate-300 text-slate-700">{row.action}</Badge>
                    <span className="text-xs text-slate-500">{fmt(row.createdAtUtc)}</span>
                  </div>
                  <p className="mt-3 text-sm text-slate-800">{row.details || "-"}</p>
                  <p className="mt-2 text-xs text-slate-500">{row.tenantName || "Platform"} · {row.ipAddress || "-"} · {row.deviceLabel || row.userAgent || "-"}</p>
                </div>
              )) : <div className="rounded-2xl border border-dashed border-slate-200 px-4 py-8 text-center text-sm text-slate-500">No audit rows found for this user under the current filters.</div>}
            </CardContent>
          </Card>
        </DialogContent>
      </Dialog>
    </div>
  );
}
