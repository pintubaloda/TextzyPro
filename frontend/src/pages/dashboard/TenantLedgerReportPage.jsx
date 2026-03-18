import { useEffect, useMemo, useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { getBillingLedgerReport } from "@/lib/api";
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

const isWithinDateRange = (value, fromDate, toDate) => {
  if (!value) return true;
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return true;
  if (fromDate && date < new Date(`${fromDate}T00:00:00`)) return false;
  if (toDate && date > new Date(`${toDate}T23:59:59.999`)) return false;
  return true;
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

const serviceBadge = (service, apiName) => {
  const normalized = String(service || "").toLowerCase();
  const className =
    normalized === "sms"
      ? "border-sky-200 bg-sky-100 text-sky-700"
      : normalized === "whatsapp"
      ? "border-emerald-200 bg-emerald-100 text-emerald-700"
      : normalized === "kyc"
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

export default function TenantLedgerReportPage() {
  const [busy, setBusy] = useState(false);
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(null);
  const [data, setData] = useState({ summary: {}, items: [] });
  const [filters, setFilters] = useState({ service: "all", status: "all", q: "", fromDate: "", toDate: "", take: 250, page: 1, pageSize: 12 });

  const load = async (nextFilters = filters) => {
    setBusy(true);
    try {
      const res = await getBillingLedgerReport({
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
      toast.error(error?.message || "Failed to load unified ledger");
    } finally {
      setBusy(false);
    }
  };

  useEffect(() => {
    load(filters);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const services = useMemo(() => Array.isArray(data?.summary?.services) ? data.summary.services : [], [data]);
  const currentBalances = useMemo(() => data?.summary?.currentBalances || {}, [data]);
  const balanceRows = useMemo(() => Object.entries(currentBalances || {}), [currentBalances]);
  const filteredItems = useMemo(
    () => (data?.items || []).filter((row) => isWithinDateRange(row?.occurredAtUtc, filters.fromDate, filters.toDate)),
    [data?.items, filters.fromDate, filters.toDate],
  );
  const totalPages = useMemo(() => Math.max(1, Math.ceil(filteredItems.length / filters.pageSize)), [filteredItems.length, filters.pageSize]);
  const pagedItems = useMemo(() => filteredItems.slice((filters.page - 1) * filters.pageSize, filters.page * filters.pageSize), [filteredItems, filters.page, filters.pageSize]);

  useEffect(() => {
    setFilters((prev) => ({ ...prev, page: Math.min(prev.page, totalPages) }));
  }, [totalPages]);

  const exportRows = () => {
    downloadCsv("tenant-ledger-report.csv", [
      ["Date", "API Name", "Service", "Event", "Reference ID", "Customer Ref", "Credits Used", "Units", "Amount", "Status", "Recipient", "Description"],
      ...filteredItems.map((row) => [
        formatUtc(row?.occurredAtUtc),
        row?.apiName || "",
        row?.service || "",
        row?.entryType || "",
        row?.referenceId || "",
        row?.customerRef || "",
        Number(row?.creditsUsed || 0),
        Number(row?.units || 0),
        row?.amount != null ? money(row.amount, row.currency) : "",
        statusMeta(row?.status).label,
        row?.recipient || "",
        row?.description || "",
      ]),
    ]);
    toast.success("Ledger exported");
  };

  return (
    <div className="space-y-6">
      <Card className="border-slate-200 shadow-sm">
        <CardHeader className="flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
          <div>
            <CardTitle>Unified Ledger</CardTitle>
            <CardDescription>One strong ledger for SMS, WhatsApp, KYC, and billing purchases with credits, references, and status visibility.</CardDescription>
          </div>
          <div className="flex flex-wrap gap-2">
            <Button variant="outline" onClick={exportRows} disabled={!data?.items?.length}>Export CSV</Button>
            <Button className="bg-orange-500 hover:bg-orange-600" onClick={() => load(filters)} disabled={busy}>
              {busy ? "Loading..." : "Refresh"}
            </Button>
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
            <SummaryCard title="Ledger Rows" value={Number(data?.summary?.totalEntries || 0).toLocaleString()} hint="Filtered entries across all services" />
            <SummaryCard title="Invoice Value" value={money(data?.summary?.totalAmount || 0)} hint="Paid and issued billing value in the current ledger view" />
            <SummaryCard title="Credits Used" value={Number(data?.summary?.totalCreditsUsed || 0).toLocaleString()} hint="KYC and credit-based consumption" />
            <SummaryCard title="Usage Units" value={Number(data?.summary?.totalUnits || 0).toLocaleString()} hint="SMS segments, messages, and KYC units combined" />
          </div>

          <div className="grid gap-4 lg:grid-cols-[1.4fr_1fr]">
            <Card className="border-slate-200">
              <CardHeader>
                <CardTitle className="text-base">Filters</CardTitle>
                <CardDescription>Slice the ledger by service, status, or any reference/customer text.</CardDescription>
              </CardHeader>
              <CardContent className="grid gap-4 md:grid-cols-4">
                <div className="space-y-2">
                  <Label>Service</Label>
                  <Select value={filters.service} onValueChange={(value) => setFilters((prev) => ({ ...prev, service: value, page: 1 }))}>
                    <SelectTrigger><SelectValue /></SelectTrigger>
                    <SelectContent>
                      <SelectItem value="all">All services</SelectItem>
                      <SelectItem value="sms">SMS</SelectItem>
                      <SelectItem value="whatsapp">WhatsApp</SelectItem>
                      <SelectItem value="kyc">KYC</SelectItem>
                      <SelectItem value="billing">Billing</SelectItem>
                    </SelectContent>
                  </Select>
                </div>
                <div className="space-y-2">
                  <Label>Status</Label>
                  <Select value={filters.status} onValueChange={(value) => setFilters((prev) => ({ ...prev, status: value, page: 1 }))}>
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
                    onChange={(event) => setFilters((prev) => ({ ...prev, q: event.target.value, page: 1 }))}
                    onKeyDown={(event) => {
                      if (event.key === "Enter") load(filters);
                    }}
                    placeholder="Search ref id, customer ref, API name, recipient"
                  />
                </div>
                <div className="space-y-2">
                  <Label>From date</Label>
                  <Input type="date" value={filters.fromDate} onChange={(event) => setFilters((prev) => ({ ...prev, fromDate: event.target.value, page: 1 }))} />
                </div>
                <div className="space-y-2">
                  <Label>To date</Label>
                  <Input type="date" value={filters.toDate} onChange={(event) => setFilters((prev) => ({ ...prev, toDate: event.target.value, page: 1 }))} />
                </div>
                <div className="md:col-span-4 flex flex-wrap gap-2">
                  <Button className="bg-slate-900 hover:bg-slate-800" onClick={() => load(filters)} disabled={busy}>Apply Filters</Button>
                  <Button variant="outline" onClick={() => {
                    const reset = { service: "all", status: "all", q: "", fromDate: "", toDate: "", take: 250, page: 1, pageSize: 12 };
                    setFilters(reset);
                    load(reset);
                  }}>Reset</Button>
                </div>
              </CardContent>
            </Card>

            <Card className="border-slate-200">
              <CardHeader>
                <CardTitle className="text-base">Live Balances</CardTitle>
                <CardDescription>Current remaining balances from the billing engine.</CardDescription>
              </CardHeader>
              <CardContent className="space-y-3">
                {balanceRows.length ? balanceRows.map(([metric, units]) => (
                  <div key={metric} className="flex items-center justify-between rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3">
                    <div>
                      <div className="text-xs uppercase tracking-[0.18em] text-slate-500">{metric}</div>
                      <div className="text-sm font-medium text-slate-900">Available balance</div>
                    </div>
                    <div className="text-lg font-semibold text-slate-950">{Number(units || 0).toLocaleString()}</div>
                  </div>
                )) : <div className="rounded-2xl border border-dashed border-slate-200 px-4 py-8 text-center text-sm text-slate-500">No live balances available.</div>}
              </CardContent>
            </Card>
          </div>

          {services.length ? (
            <div className="flex flex-wrap gap-2">
              {services.map((row) => (
                <div key={row.service} className="rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm shadow-sm">
                  <div className="flex items-center gap-2">
                    {serviceBadge(row.service, row.apiName)}
                    <span className="font-medium text-slate-900">{Number(row.count || 0).toLocaleString()} rows</span>
                  </div>
                  <div className="mt-1 text-xs text-slate-500">
                    Credits {Number(row.creditsUsed || 0).toLocaleString()} | Amount {money(row.amount || 0)}
                  </div>
                </div>
              ))}
            </div>
          ) : null}
        </CardContent>
      </Card>

      <Card className="border-slate-200 shadow-sm">
        <CardHeader>
          <CardTitle>Ledger Timeline</CardTitle>
          <CardDescription>Credits, purchases, verifications, and delivery activity in one chronological table.</CardDescription>
        </CardHeader>
        <CardContent>
          <div className="overflow-auto rounded-2xl border border-slate-200">
            <table className="min-w-full text-left text-sm">
              <thead className="sticky top-0 z-10 bg-slate-50 text-slate-600 shadow-sm">
                <tr>
                  {["Date", "API Name", "Event", "Ref ID", "Customer Ref", "Credit Used", "Amount", "Status", "View"].map((header) => (
                    <th key={header} className="px-4 py-3">{header}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {!pagedItems?.length ? (
                  <tr>
                    <td colSpan={9} className="px-4 py-12 text-center text-slate-500">{busy ? "Loading unified ledger..." : "No ledger entries match the current filters."}</td>
                  </tr>
                ) : (
                  pagedItems.map((row) => {
                    const status = statusMeta(row?.status);
                    return (
                      <tr key={`${row.id}-${row.referenceId}`} className="border-t border-slate-200">
                        <td className="px-4 py-3 text-slate-500">{formatUtc(row.occurredAtUtc)}</td>
                        <td className="px-4 py-3">{serviceBadge(row.service, row.apiName)}</td>
                        <td className="px-4 py-3">
                          <div className="font-medium text-slate-900">{row.entryType || "-"}</div>
                          <div className="text-xs text-slate-500">{row.description || "-"}</div>
                        </td>
                        <td className="px-4 py-3 font-medium text-slate-900">{row.referenceId || row.externalReference || "-"}</td>
                        <td className="px-4 py-3 text-slate-700">{row.customerRef || "-"}</td>
                        <td className="px-4 py-3 text-slate-700">{Number(row.creditsUsed || row.units || 0).toLocaleString()}</td>
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
          <div className="mt-4 flex flex-col gap-3 rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3 md:flex-row md:items-center md:justify-between">
            <div className="text-sm text-slate-600">Showing {filteredItems.length ? ((filters.page - 1) * filters.pageSize) + 1 : 0} - {Math.min(filters.page * filters.pageSize, filteredItems.length)} of {filteredItems.length.toLocaleString()} rows</div>
            <div className="flex items-center gap-2">
              <Select value={String(filters.pageSize)} onValueChange={(value) => setFilters((prev) => ({ ...prev, pageSize: Number(value), page: 1 }))}>
                <SelectTrigger className="h-9 w-[110px]"><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="12">12 / page</SelectItem>
                  <SelectItem value="25">25 / page</SelectItem>
                  <SelectItem value="50">50 / page</SelectItem>
                </SelectContent>
              </Select>
              <Button variant="outline" className="rounded-xl" disabled={filters.page <= 1} onClick={() => setFilters((prev) => ({ ...prev, page: prev.page - 1 }))}>Previous</Button>
              <div className="min-w-[90px] text-center text-sm text-slate-600">Page {filters.page} / {totalPages}</div>
              <Button variant="outline" className="rounded-xl" disabled={filters.page >= totalPages} onClick={() => setFilters((prev) => ({ ...prev, page: prev.page + 1 }))}>Next</Button>
            </div>
          </div>
        </CardContent>
      </Card>

      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent className="max-w-4xl">
          <DialogHeader>
            <DialogTitle>Ledger Entry Detail</DialogTitle>
          </DialogHeader>
          {active ? (
            <div className="space-y-4">
              <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
                <Card className="border-slate-200"><CardContent className="pt-4"><div className="text-xs uppercase text-slate-500">API Name</div><div className="mt-2">{serviceBadge(active.service, active.apiName)}</div></CardContent></Card>
                <Card className="border-slate-200"><CardContent className="pt-4"><div className="text-xs uppercase text-slate-500">Status</div><div className="mt-2"><Badge className={statusMeta(active.status).className}>{statusMeta(active.status).label}</Badge></div></CardContent></Card>
                <Card className="border-slate-200"><CardContent className="pt-4"><div className="text-xs uppercase text-slate-500">Credits Used</div><div className="mt-2 text-lg font-semibold text-slate-950">{Number(active.creditsUsed || active.units || 0).toLocaleString()}</div></CardContent></Card>
                <Card className="border-slate-200"><CardContent className="pt-4"><div className="text-xs uppercase text-slate-500">Amount</div><div className="mt-2 text-lg font-semibold text-slate-950">{active.amount != null ? money(active.amount, active.currency) : "-"}</div></CardContent></Card>
              </div>
              <div className="grid gap-3 rounded-2xl border border-slate-200 bg-slate-50 p-4 md:grid-cols-2">
                <div><div className="text-xs uppercase text-slate-500">Reference ID</div><div className="mt-1 font-medium text-slate-900 break-all">{active.referenceId || "-"}</div></div>
                <div><div className="text-xs uppercase text-slate-500">External Reference</div><div className="mt-1 font-medium text-slate-900 break-all">{active.externalReference || "-"}</div></div>
                <div><div className="text-xs uppercase text-slate-500">Customer Ref</div><div className="mt-1 font-medium text-slate-900 break-all">{active.customerRef || "-"}</div></div>
                <div><div className="text-xs uppercase text-slate-500">Recipient</div><div className="mt-1 font-medium text-slate-900 break-all">{active.recipient || "-"}</div></div>
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

