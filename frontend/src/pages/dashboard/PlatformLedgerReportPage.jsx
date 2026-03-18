import { useEffect, useMemo, useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { getPlatformLedgerReport } from "@/lib/api";
import { toast } from "sonner";

const formatUtc = (value) => {
  if (!value) return "-";
  try {
    return new Date(value).toLocaleString();
  } catch {
    return String(value || "-");
  }
};

const money = (value, currency = "INR") => {
  const amount = Number(value || 0);
  return `${String(currency || "INR").toUpperCase()} ${amount.toLocaleString("en-IN", { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
};

const statusMeta = (raw) => {
  const value = String(raw || "").trim();
  const lower = value.toLowerCase();
  if (!value) return { label: "-", className: "border-slate-200 bg-slate-100 text-slate-600" };
  if (["verified", "paid", "delivered", "success", "submitted", "sent", "read"].includes(lower)) {
    return { label: "Success", className: "border-emerald-200 bg-emerald-100 text-emerald-700" };
  }
  if (lower.includes("fail") || lower.includes("error") || lower.includes("reject") || lower.includes("refund")) {
    return { label: "Fail", className: "border-rose-200 bg-rose-100 text-rose-700" };
  }
  return { label: "Pending", className: "border-amber-200 bg-amber-100 text-amber-700" };
};

const apiBadge = (service, apiName) => {
  const normalized = String(service || "").toLowerCase();
  const className =
    normalized === "kyc"
      ? "border-violet-200 bg-violet-100 text-violet-700"
      : "border-orange-200 bg-orange-100 text-orange-700";
  return <Badge className={className}>{apiName || service || "-"}</Badge>;
};

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

export default function PlatformLedgerReportPage() {
  const [busy, setBusy] = useState(false);
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(null);
  const [data, setData] = useState({ summary: {}, items: [] });
  const [filters, setFilters] = useState({ service: "all", status: "all", q: "", take: 300 });

  const load = async (nextFilters = filters) => {
    setBusy(true);
    try {
      const res = await getPlatformLedgerReport({
        service: nextFilters.service === "all" ? "" : nextFilters.service,
        status: nextFilters.status === "all" ? "" : nextFilters.status,
        q: nextFilters.q,
        take: nextFilters.take,
      });
      setData({
        summary: res?.summary || {},
        items: Array.isArray(res?.items) ? res.items : [],
      });
    } catch (error) {
      toast.error(error?.message || "Failed to load platform ledger");
    } finally {
      setBusy(false);
    }
  };

  useEffect(() => {
    load(filters);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const balances = useMemo(() => Array.isArray(data?.summary?.balanceSnapshot) ? data.summary.balanceSnapshot : [], [data]);

  return (
    <div className="space-y-6" data-testid="platform-ledger-report-page">
      <Card className="border-slate-200 shadow-sm">
        <CardHeader className="flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
          <div>
            <CardTitle>Platform Ledger</CardTitle>
            <CardDescription>Strong owner-side ledger for KYC verification events, invoice-backed purchases, tenant references, and live balance snapshot.</CardDescription>
          </div>
          <Button className="bg-blue-600 text-white hover:bg-blue-700" onClick={() => load(filters)} disabled={busy}>
            {busy ? "Loading..." : "Refresh"}
          </Button>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
            <SummaryCard title="Ledger Rows" value={Number(data?.summary?.totalEntries || 0).toLocaleString()} hint="Filtered platform ledger entries" />
            <SummaryCard title="Invoice Value" value={money(data?.summary?.totalInvoiceValue || 0)} hint="Purchase-side invoice total in the current view" />
            <SummaryCard title="Credits Used" value={Number(data?.summary?.totalCreditsUsed || 0).toLocaleString()} hint="KYC billable credits consumed" />
            <SummaryCard title="Unique Tenants" value={Number(data?.summary?.uniqueTenants || 0).toLocaleString()} hint="Customers represented in the current result set" />
          </div>

          <Card className="border-slate-200">
            <CardHeader>
              <CardTitle className="text-base">Filters</CardTitle>
              <CardDescription>Focus the ledger by service, outcome, or tenant/reference text.</CardDescription>
            </CardHeader>
            <CardContent className="grid gap-4 md:grid-cols-4">
              <div className="space-y-2">
                <Label>Service</Label>
                <Select value={filters.service} onValueChange={(value) => setFilters((prev) => ({ ...prev, service: value }))}>
                  <SelectTrigger><SelectValue /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value="all">All services</SelectItem>
                    <SelectItem value="billing">Billing</SelectItem>
                    <SelectItem value="kyc">KYC</SelectItem>
                  </SelectContent>
                </Select>
              </div>
              <div className="space-y-2">
                <Label>Status</Label>
                <Select value={filters.status} onValueChange={(value) => setFilters((prev) => ({ ...prev, status: value }))}>
                  <SelectTrigger><SelectValue /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value="all">All status</SelectItem>
                    <SelectItem value="success">Success</SelectItem>
                    <SelectItem value="fail">Fail</SelectItem>
                    <SelectItem value="pending">Pending</SelectItem>
                  </SelectContent>
                </Select>
              </div>
              <div className="space-y-2 md:col-span-2">
                <Label>Search</Label>
                <Input
                  value={filters.q}
                  onChange={(event) => setFilters((prev) => ({ ...prev, q: event.target.value }))}
                  onKeyDown={(event) => {
                    if (event.key === "Enter") load(filters);
                  }}
                  placeholder="Search tenant, company, customer ref, invoice, GST"
                />
              </div>
              <div className="md:col-span-4 flex flex-wrap gap-2">
                <Button className="bg-slate-900 hover:bg-slate-800" onClick={() => load(filters)} disabled={busy}>Apply Filters</Button>
                <Button variant="outline" onClick={() => {
                  const reset = { service: "all", status: "all", q: "", take: 300 };
                  setFilters(reset);
                  load(reset);
                }}>Reset</Button>
              </div>
            </CardContent>
          </Card>

          <Card className="border-slate-200">
            <CardHeader>
              <CardTitle className="text-base">Balance Snapshot</CardTitle>
              <CardDescription>Current tenant credit balances grouped by metric key.</CardDescription>
            </CardHeader>
            <CardContent className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
              {balances.length ? balances.map((row) => (
                <div key={row.metricKey} className="rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3">
                  <div className="text-xs uppercase tracking-[0.18em] text-slate-500">{row.metricKey}</div>
                  <div className="mt-2 text-2xl font-semibold text-slate-950">{Number(row.unitsRemaining || 0).toLocaleString()}</div>
                  <div className="text-xs text-slate-500">{Number(row.tenants || 0).toLocaleString()} tenants</div>
                </div>
              )) : <div className="rounded-2xl border border-dashed border-slate-200 px-4 py-8 text-center text-sm text-slate-500 md:col-span-2 xl:col-span-4">No balance snapshot available.</div>}
            </CardContent>
          </Card>
        </CardContent>
      </Card>

      <Card className="border-slate-200 shadow-sm">
        <CardHeader>
          <CardTitle>Platform Ledger Timeline</CardTitle>
          <CardDescription>Track tenant-wise purchases and KYC credit events with stronger operational context.</CardDescription>
        </CardHeader>
        <CardContent>
          <div className="overflow-auto rounded-2xl border border-slate-200">
            <table className="min-w-full text-left text-sm">
              <thead className="bg-slate-50 text-slate-600">
                <tr>
                  {["Date", "Tenant", "API Name", "Event", "Ref ID", "Customer Ref", "Credit Used", "Amount", "Status", "View"].map((header) => (
                    <th key={header} className="px-4 py-3">{header}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {!data?.items?.length ? (
                  <tr>
                    <td colSpan={10} className="px-4 py-12 text-center text-slate-500">{busy ? "Loading platform ledger..." : "No platform ledger entries match the current filters."}</td>
                  </tr>
                ) : (
                  data.items.map((row) => {
                    const status = statusMeta(row?.status);
                    return (
                      <tr key={`${row.id}-${row.referenceId}`} className="border-t border-slate-200">
                        <td className="px-4 py-3 text-slate-500">{formatUtc(row.occurredAtUtc)}</td>
                        <td className="px-4 py-3">
                          <div className="font-medium text-slate-900">{row.companyName || row.tenantName || "-"}</div>
                          <div className="text-xs text-slate-500">{row.tenantSlug || "-"}</div>
                        </td>
                        <td className="px-4 py-3">{apiBadge(row.service, row.apiName)}</td>
                        <td className="px-4 py-3">
                          <div className="font-medium text-slate-900">{row.entryType || "-"}</div>
                          <div className="text-xs text-slate-500">{row.description || "-"}</div>
                        </td>
                        <td className="px-4 py-3 font-medium text-slate-900">{row.referenceId || row.externalReference || "-"}</td>
                        <td className="px-4 py-3 text-slate-700">{row.customerRef || "-"}</td>
                        <td className="px-4 py-3 text-slate-700">{Number(row.creditsUsed || 0).toLocaleString()}</td>
                        <td className="px-4 py-3 text-slate-700">{row.amount != null ? money(row.amount, row.currency) : "-"}</td>
                        <td className="px-4 py-3"><Badge className={status.className}>{status.label}</Badge></td>
                        <td className="px-4 py-3 text-right">
                          <Button variant="outline" className="rounded-xl" onClick={() => { setActive(row); setOpen(true); }}>
                            View
                          </Button>
                        </td>
                      </tr>
                    );
                  })
                )}
              </tbody>
            </table>
          </div>
        </CardContent>
      </Card>

      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent className="max-w-4xl">
          <DialogHeader>
            <DialogTitle>Platform Ledger Entry Detail</DialogTitle>
          </DialogHeader>
          {active ? (
            <div className="space-y-4">
              <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
                <Card className="border-slate-200"><CardContent className="pt-4"><div className="text-xs uppercase text-slate-500">Tenant</div><div className="mt-2 font-semibold text-slate-950">{active.companyName || active.tenantName || "-"}</div><div className="text-xs text-slate-500">{active.tenantSlug || "-"}</div></CardContent></Card>
                <Card className="border-slate-200"><CardContent className="pt-4"><div className="text-xs uppercase text-slate-500">API Name</div><div className="mt-2">{apiBadge(active.service, active.apiName)}</div></CardContent></Card>
                <Card className="border-slate-200"><CardContent className="pt-4"><div className="text-xs uppercase text-slate-500">Status</div><div className="mt-2"><Badge className={statusMeta(active.status).className}>{statusMeta(active.status).label}</Badge></div></CardContent></Card>
                <Card className="border-slate-200"><CardContent className="pt-4"><div className="text-xs uppercase text-slate-500">Value</div><div className="mt-2 text-lg font-semibold text-slate-950">{active.amount != null ? money(active.amount, active.currency) : `${Number(active.creditsUsed || 0).toLocaleString()} credits`}</div></CardContent></Card>
              </div>
              <div className="grid gap-3 rounded-2xl border border-slate-200 bg-slate-50 p-4 md:grid-cols-2">
                <div><div className="text-xs uppercase text-slate-500">Reference ID</div><div className="mt-1 font-medium text-slate-900 break-all">{active.referenceId || "-"}</div></div>
                <div><div className="text-xs uppercase text-slate-500">External Reference</div><div className="mt-1 font-medium text-slate-900 break-all">{active.externalReference || "-"}</div></div>
                <div><div className="text-xs uppercase text-slate-500">Customer Ref</div><div className="mt-1 font-medium text-slate-900 break-all">{active.customerRef || "-"}</div></div>
                <div><div className="text-xs uppercase text-slate-500">Occurred At</div><div className="mt-1 font-medium text-slate-900">{formatUtc(active.occurredAtUtc)}</div></div>
                <div className="md:col-span-2"><div className="text-xs uppercase text-slate-500">Description</div><div className="mt-1 font-medium text-slate-900 whitespace-pre-wrap">{active.description || "-"}</div></div>
              </div>
              <div className="rounded-2xl border border-slate-200 bg-white p-3">
                <div className="text-xs font-medium uppercase tracking-[0.18em] text-slate-500">Raw Ledger Entry</div>
                <pre className="mt-2 max-h-[420px] overflow-auto rounded-xl bg-slate-50 p-3 text-xs text-slate-700">{JSON.stringify(active, null, 2)}</pre>
              </div>
            </div>
          ) : null}
        </DialogContent>
      </Dialog>
    </div>
  );
}
