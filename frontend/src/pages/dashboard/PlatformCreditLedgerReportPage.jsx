import { useEffect, useMemo, useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { getPlatformCreditLedgerReport, getPlatformCustomers } from "@/lib/api";
import { toast } from "sonner";

const formatUtc = (value) => {
  if (!value) return "-";
  try {
    return new Date(value).toLocaleString();
  } catch {
    return String(value || "-");
  }
};

const statusMeta = (raw) => {
  const value = String(raw || "").trim();
  const lower = value.toLowerCase();
  if (!value) return { label: "-", className: "border-slate-200 bg-slate-100 text-slate-600" };
  if (lower.includes("refund") || lower.includes("fail") || lower.includes("error")) {
    return { label: "Fail", className: "border-rose-200 bg-rose-100 text-rose-700" };
  }
  if (["applied", "success", "completed"].includes(lower)) {
    return { label: "Success", className: "border-emerald-200 bg-emerald-100 text-emerald-700" };
  }
  return { label: "Pending", className: "border-amber-200 bg-amber-100 text-amber-700" };
};

const txBadge = (value) => {
  const lower = String(value || "").toLowerCase();
  const className =
    lower === "debit"
      ? "border-rose-200 bg-rose-100 text-rose-700"
      : lower === "refund"
      ? "border-amber-200 bg-amber-100 text-amber-700"
      : "border-emerald-200 bg-emerald-100 text-emerald-700";
  return <Badge className={className}>{value || "-"}</Badge>;
};

function downloadCsv(filename, rows) {
  const csv = rows.map((cols) => cols.map((value) => {
    const normalized = String(value ?? "");
    return /[",\n]/.test(normalized) ? `"${normalized.replace(/"/g, '""')}"` : normalized;
  }).join(",")).join("\n");
  const blob = new Blob([csv], { type: "text/csv;charset=utf-8" });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  a.click();
  URL.revokeObjectURL(url);
}

function SummaryCard({ title, value, hint }) {
  return (
    <Card className="border-slate-200 shadow-sm">
      <CardContent className="pt-5">
        <div className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">{title}</div>
        <div className="mt-2 text-3xl font-bold text-slate-950">{value}</div>
        <div className="mt-1 text-sm text-slate-500">{hint}</div>
      </CardContent>
    </Card>
  );
}

export default function PlatformCreditLedgerReportPage() {
  const [busy, setBusy] = useState(false);
  const [data, setData] = useState({ summary: {}, items: [] });
  const [tenants, setTenants] = useState([]);
  const [filters, setFilters] = useState({ tenantId: "all", service: "all", status: "all", q: "", take: 300, page: 1, pageSize: 12 });

  const load = async (next = filters) => {
    setBusy(true);
    try {
      const [customerRows, res] = await Promise.all([
        getPlatformCustomers("").catch(() => []),
        getPlatformCreditLedgerReport({
          tenantId: next.tenantId === "all" ? "" : next.tenantId,
          service: next.service === "all" ? "" : next.service,
          status: next.status === "all" ? "" : next.status,
          q: next.q,
          take: next.take,
        }),
      ]);
      setTenants(Array.isArray(customerRows) ? customerRows : []);
      setData({
        summary: res?.summary || {},
        items: Array.isArray(res?.items) ? res.items : [],
      });
    } catch (error) {
      toast.error(error?.message || "Failed to load platform credit ledger");
    } finally {
      setBusy(false);
    }
  };

  useEffect(() => {
    load(filters);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const totalPages = useMemo(() => Math.max(1, Math.ceil((data?.items?.length || 0) / filters.pageSize)), [data?.items?.length, filters.pageSize]);
  const pagedItems = useMemo(
    () => (data?.items || []).slice((filters.page - 1) * filters.pageSize, filters.page * filters.pageSize),
    [data?.items, filters.page, filters.pageSize],
  );
  const balances = useMemo(() => Array.isArray(data?.summary?.balanceSnapshot) ? data.summary.balanceSnapshot : [], [data?.summary?.balanceSnapshot]);

  useEffect(() => {
    setFilters((prev) => ({ ...prev, page: Math.min(prev.page, totalPages) }));
  }, [totalPages]);

  const exportRows = () => {
    downloadCsv("platform-credit-ledger.csv", [
      ["Date", "Tenant", "Slug", "Metric Key", "Type", "Units", "Source", "Service", "Reference ID", "Status"],
      ...(data?.items || []).map((row) => [
        formatUtc(row?.occurredAtUtc),
        row?.companyName || row?.tenantName || "",
        row?.tenantSlug || "",
        row?.metricKey || "",
        row?.transactionType || "",
        Number(row?.units || 0),
        row?.source || "",
        row?.service || "",
        row?.referenceId || "",
        row?.status || "",
      ]),
    ]);
    toast.success("Platform credit ledger exported");
  };

  return (
    <div className="space-y-6">
      <Card className="border-slate-200 shadow-sm">
        <CardHeader className="flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
          <div>
            <CardTitle>Platform Credit Ledger</CardTitle>
            <CardDescription>Owner-side debit, refund, and credit audit trail across all tenants.</CardDescription>
          </div>
          <div className="flex gap-2">
            <Button variant="outline" onClick={exportRows} disabled={!data?.items?.length}>Export CSV</Button>
            <Button className="bg-blue-600 text-white hover:bg-blue-700" onClick={() => load(filters)} disabled={busy}>
              {busy ? "Loading..." : "Refresh"}
            </Button>
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
            <SummaryCard title="Rows" value={Number(data?.summary?.totalEntries || 0).toLocaleString()} hint="Tenant credit movements in this view" />
            <SummaryCard title="Tenants" value={Number(data?.summary?.uniqueTenants || 0).toLocaleString()} hint="Customers included in the result set" />
            <SummaryCard title="Debited" value={Number(data?.summary?.debitUnits || 0).toLocaleString()} hint="Units consumed across tenants" />
            <SummaryCard title="Refunded" value={Number(data?.summary?.refundUnits || 0).toLocaleString()} hint="Units returned across tenants" />
          </div>

          <div className="grid gap-4 lg:grid-cols-[1.3fr_1fr]">
            <Card className="border-slate-200">
              <CardHeader>
                <CardTitle className="text-base">Filters</CardTitle>
                <CardDescription>Search by tenant, metric key, source, service, or reference id.</CardDescription>
              </CardHeader>
              <CardContent className="grid gap-4 md:grid-cols-4">
                <div className="space-y-2">
                  <Label>Tenant slug</Label>
                  <Select value={filters.tenantId} onValueChange={(value) => setFilters((prev) => ({ ...prev, tenantId: value, page: 1 }))}>
                    <SelectTrigger><SelectValue /></SelectTrigger>
                    <SelectContent>
                      <SelectItem value="all">All tenants</SelectItem>
                      {tenants.map((tenant) => (
                        <SelectItem key={tenant.tenantId} value={tenant.tenantId}>
                          {tenant.tenantSlug || tenant.companyName || tenant.tenantName || tenant.tenantId}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>
                <div className="space-y-2">
                  <Label>Service</Label>
                  <Select value={filters.service} onValueChange={(value) => setFilters((prev) => ({ ...prev, service: value, page: 1 }))}>
                    <SelectTrigger><SelectValue /></SelectTrigger>
                    <SelectContent>
                      <SelectItem value="all">All services</SelectItem>
                      <SelectItem value="sms">SMS</SelectItem>
                      <SelectItem value="whatsapp">WhatsApp</SelectItem>
                      <SelectItem value="digilocker">DigiLocker</SelectItem>
                      <SelectItem value="gst">GST</SelectItem>
                    </SelectContent>
                  </Select>
                </div>
                <div className="space-y-2">
                  <Label>Status</Label>
                  <Select value={filters.status} onValueChange={(value) => setFilters((prev) => ({ ...prev, status: value, page: 1 }))}>
                    <SelectTrigger><SelectValue /></SelectTrigger>
                    <SelectContent>
                      <SelectItem value="all">All status</SelectItem>
                      <SelectItem value="applied">Applied</SelectItem>
                      <SelectItem value="refunded">Refunded</SelectItem>
                      <SelectItem value="pending">Pending</SelectItem>
                    </SelectContent>
                  </Select>
                </div>
                <div className="space-y-2 md:col-span-2">
                  <Label>Search</Label>
                  <Input
                    value={filters.q}
                    onChange={(event) => setFilters((prev) => ({ ...prev, q: event.target.value, page: 1 }))}
                    onKeyDown={(event) => {
                      if (event.key === "Enter") load(filters);
                    }}
                    placeholder="metric key, source, service, ref id"
                  />
                </div>
                <div className="md:col-span-4 flex flex-wrap gap-2">
                  <Button className="bg-slate-900 hover:bg-slate-800" onClick={() => load(filters)} disabled={busy}>Apply Filters</Button>
                  <Button variant="outline" onClick={() => {
                    const reset = { tenantId: "all", service: "all", status: "all", q: "", take: 300, page: 1, pageSize: 12 };
                    setFilters(reset);
                    load(reset);
                  }}>Reset</Button>
                </div>
              </CardContent>
            </Card>

            <Card className="border-slate-200">
              <CardHeader>
                <CardTitle className="text-base">Balance Snapshot</CardTitle>
                <CardDescription>Current balances grouped by metric key.</CardDescription>
              </CardHeader>
              <CardContent className="space-y-3">
                {balances.length ? balances.map((row) => (
                  <div key={row.metricKey} className="flex items-center justify-between rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3">
                    <div>
                      <div className="text-xs uppercase tracking-[0.18em] text-slate-500">{row.metricKey}</div>
                      <div className="text-sm font-medium text-slate-900">{Number(row.tenants || 0).toLocaleString()} tenants</div>
                    </div>
                    <div className="text-lg font-semibold text-slate-950">{Number(row.unitsRemaining || 0).toLocaleString()}</div>
                  </div>
                )) : <div className="rounded-2xl border border-dashed border-slate-200 px-4 py-8 text-center text-sm text-slate-500">No balances yet.</div>}
              </CardContent>
            </Card>
          </div>
        </CardContent>
      </Card>

      <Card className="border-slate-200 shadow-sm">
        <CardHeader>
          <CardTitle>Transactions</CardTitle>
          <CardDescription>Simple platform view of all debit, refund, and credit events.</CardDescription>
        </CardHeader>
        <CardContent>
          <div className="overflow-auto rounded-2xl border border-slate-200">
            <table className="min-w-full text-left text-sm">
              <thead className="sticky top-0 z-10 bg-slate-50 text-slate-600 shadow-sm">
                <tr>
                  {["Date", "Tenant", "Metric", "Type", "Units", "Source", "Service", "Reference", "Status"].map((header) => (
                    <th key={header} className="px-4 py-3">{header}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {pagedItems.length ? pagedItems.map((row) => (
                  <tr key={row.id} className="border-t border-slate-100 align-top">
                    <td className="px-4 py-3 whitespace-nowrap text-slate-600">{formatUtc(row.occurredAtUtc)}</td>
                    <td className="px-4 py-3">
                      <div className="font-medium text-slate-900">{row.companyName || row.tenantName || "-"}</div>
                      <div className="text-xs text-slate-500">{row.tenantSlug || "-"}</div>
                    </td>
                    <td className="px-4 py-3 font-medium text-slate-900">{row.metricKey || "-"}</td>
                    <td className="px-4 py-3">{txBadge(row.transactionType)}</td>
                    <td className="px-4 py-3 text-slate-700">{Number(row.units || 0).toLocaleString()}</td>
                    <td className="px-4 py-3 text-slate-700">{row.source || "-"}</td>
                    <td className="px-4 py-3 text-slate-700">{row.service || "-"}</td>
                    <td className="px-4 py-3 font-mono text-xs text-slate-600">{row.referenceId || "-"}</td>
                    <td className="px-4 py-3"><Badge className={statusMeta(row.status).className}>{statusMeta(row.status).label}</Badge></td>
                  </tr>
                )) : (
                  <tr>
                    <td colSpan={9} className="px-4 py-10 text-center text-sm text-slate-500">No platform credit ledger entries found.</td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>

          <div className="mt-4 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
            <div className="text-sm text-slate-500">
              Showing {pagedItems.length ? (filters.page - 1) * filters.pageSize + 1 : 0}-{Math.min(filters.page * filters.pageSize, data?.items?.length || 0)} of {Number(data?.items?.length || 0).toLocaleString()} credit rows
            </div>
            <div className="flex flex-wrap items-center gap-2">
              <Label className="text-xs text-slate-500">Rows</Label>
              <Select value={String(filters.pageSize)} onValueChange={(value) => setFilters((prev) => ({ ...prev, pageSize: Number(value), page: 1 }))}>
                <SelectTrigger className="w-[90px]"><SelectValue /></SelectTrigger>
                <SelectContent>
                  {[10, 20, 50].map((size) => <SelectItem key={size} value={String(size)}>{size}</SelectItem>)}
                </SelectContent>
              </Select>
              <Button variant="outline" onClick={() => setFilters((prev) => ({ ...prev, page: Math.max(1, prev.page - 1) }))} disabled={filters.page <= 1}>Previous</Button>
              <Button variant="outline" onClick={() => setFilters((prev) => ({ ...prev, page: Math.min(totalPages, prev.page + 1) }))} disabled={filters.page >= totalPages}>Next</Button>
            </div>
          </div>
        </CardContent>
      </Card>
    </div>
  );
}
