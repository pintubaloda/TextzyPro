import { useEffect, useMemo, useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { ChevronLeft, ChevronRight, CreditCard, Eye, Filter, Mail, RefreshCcw, ReceiptText, Search, Wallet } from "lucide-react";
import { toast } from "sonner";
import {
  getPlatformPurchaseReport,
  sendPlatformPurchaseInvoice,
  viewPlatformPurchaseInvoice,
} from "@/lib/api";

const dateToUtcStart = (value) => (value ? new Date(`${value}T00:00:00`).toISOString() : "");
const dateToUtcEnd = (value) => (value ? new Date(`${value}T23:59:59`).toISOString() : "");

const money = (value, currency = "INR") => {
  const code = String(currency || "INR").toUpperCase();
  return `${code === "INR" ? "INR" : code} ${Number(value || 0).toLocaleString("en-IN", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })}`;
};

const fmtDate = (value) => {
  if (!value) return "-";
  try {
    return new Date(value).toLocaleDateString();
  } catch {
    return String(value);
  }
};

function SummaryCard({ title, value, hint, icon: Icon, tone = "orange" }) {
  const tones = {
    orange: "border-orange-200 bg-orange-50 text-orange-700",
    emerald: "border-emerald-200 bg-emerald-50 text-emerald-700",
    blue: "border-blue-200 bg-blue-50 text-blue-700",
    violet: "border-violet-200 bg-violet-50 text-violet-700",
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

function StatusBadge({ status }) {
  const normalized = String(status || "unknown").toLowerCase();
  const className =
    normalized === "paid"
      ? "bg-emerald-100 text-emerald-700"
      : normalized === "issued"
      ? "bg-blue-100 text-blue-700"
      : normalized === "refunded"
      ? "bg-rose-100 text-rose-700"
      : "bg-slate-100 text-slate-700";
  return <Badge className={className}>{normalized}</Badge>;
}

export default function PlatformPurchaseReportPage() {
  const defaultFrom = "";
  const defaultTo = "";

  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [sendingInvoiceId, setSendingInvoiceId] = useState("");
  const [report, setReport] = useState({
    summary: {},
    serviceOptions: [],
    items: [],
    page: 1,
    pageSize: 50,
    totalCount: 0,
    totalPages: 1,
    hasPreviousPage: false,
    hasNextPage: false,
  });
  const [filters, setFilters] = useState({
    fromDate: defaultFrom,
    toDate: defaultTo,
    service: "",
    status: "all",
    q: "",
    page: 1,
    pageSize: 50,
  });

  const load = async (activeFilters = filters) => {
    const firstLoad = !report?.items?.length && !refreshing;
    if (firstLoad) setLoading(true);
    else setRefreshing(true);

    try {
      const data = await getPlatformPurchaseReport({
        fromUtc: dateToUtcStart(activeFilters.fromDate),
        toUtc: dateToUtcEnd(activeFilters.toDate),
        service: activeFilters.service,
        status: activeFilters.status === "all" ? "" : activeFilters.status,
        q: activeFilters.q,
        page: activeFilters.page,
        pageSize: activeFilters.pageSize,
      });
      setReport(data || {
        summary: {},
        serviceOptions: [],
        items: [],
        page: 1,
        pageSize: activeFilters.pageSize,
        totalCount: 0,
        totalPages: 1,
        hasPreviousPage: false,
        hasNextPage: false,
      });
    } catch (error) {
      toast.error(error?.message || "Failed to load purchase report");
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  };

  useEffect(() => {
    load(filters);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const rows = useMemo(() => (Array.isArray(report?.items) ? report.items : []), [report]);
  const summary = report?.summary || {};
  const serviceOptions = useMemo(() => (Array.isArray(report?.serviceOptions) ? report.serviceOptions : []), [report]);

  const applyFilters = async (patch = {}) => {
    const next = { ...filters, ...patch, page: patch.page ?? 1 };
    setFilters(next);
    await load(next);
  };

  const applyQuickRange = async (days) => {
    const end = new Date();
    const start = new Date();
    start.setDate(end.getDate() - days);
    await applyFilters({
      fromDate: start.toISOString().slice(0, 10),
      toDate: end.toISOString().slice(0, 10),
    });
  };

  const handleViewInvoice = async (row) => {
    try {
      const blob = await viewPlatformPurchaseInvoice(row.invoiceId);
      const url = window.URL.createObjectURL(blob);
      const win = window.open(url, "_blank", "noopener,noreferrer");
      if (!win) {
        const a = document.createElement("a");
        a.href = url;
        a.download = `${row.invoiceNo || "invoice"}.html`;
        document.body.appendChild(a);
        a.click();
        a.remove();
      }
      window.setTimeout(() => window.URL.revokeObjectURL(url), 60_000);
    } catch (error) {
      toast.error(error?.message || "Failed to open invoice");
    }
  };

  const handleResendInvoice = async (row) => {
    try {
      setSendingInvoiceId(row.invoiceId);
      await sendPlatformPurchaseInvoice(row.invoiceId);
      toast.success("Invoice resent to the stored billing recipient.");
    } catch (error) {
      toast.error(error?.message || "Failed to resend invoice");
    } finally {
      setSendingInvoiceId("");
    }
  };

  const changePage = async (nextPage) => {
    const page = Math.max(1, nextPage);
    const next = { ...filters, page };
    setFilters(next);
    await load(next);
  };

  return (
    <div className="space-y-6" data-testid="platform-purchase-report-page">
      <section className="overflow-hidden rounded-[28px] border border-slate-200 bg-[radial-gradient(circle_at_top_left,_rgba(59,130,246,0.18),_transparent_28%),linear-gradient(135deg,#eff6ff_0%,#ffffff_42%,#f8fafc_100%)] p-6 shadow-sm">
        <div className="flex flex-col gap-6 xl:flex-row xl:items-end xl:justify-between">
          <div className="max-w-3xl">
            <Badge className="border-blue-200 bg-white/80 px-3 py-1 text-[11px] font-semibold uppercase tracking-[0.22em] text-blue-700 shadow-sm">
              Platform Owner Report
            </Badge>
            <h1 className="mt-4 text-3xl font-bold tracking-tight text-slate-950 md:text-4xl">Purchase report across every billed customer account</h1>
            <p className="mt-3 max-w-2xl text-sm leading-6 text-slate-600 md:text-base">
              Review invoice-backed purchases with owner name, company, GST number, service, amount, GST, invoice date, and invoice actions from one reporting surface.
            </p>
          </div>
          <div className="flex flex-wrap gap-3">
            <Button variant="outline" className="h-11 rounded-xl border-slate-300 bg-white/80 px-5" onClick={() => applyQuickRange(7)}>
              Last 7 days
            </Button>
            <Button variant="outline" className="h-11 rounded-xl border-slate-300 bg-white/80 px-5" onClick={() => applyQuickRange(30)}>
              Last 30 days
            </Button>
            <Button variant="outline" className="h-11 rounded-xl border-slate-300 bg-white/80 px-5" onClick={() => applyFilters({ fromDate: "", toDate: "" })}>
              All time
            </Button>
            <Button className="h-11 rounded-xl bg-blue-600 px-5 text-white hover:bg-blue-700" onClick={() => load(filters)} disabled={refreshing}>
              <RefreshCcw className={`mr-2 h-4 w-4 ${refreshing ? "animate-spin" : ""}`} />
              Refresh report
            </Button>
          </div>
        </div>
      </section>

      <div className="grid gap-4 md:grid-cols-2 2xl:grid-cols-4">
        <SummaryCard title="Purchases" value={Number(summary.totalPurchases || 0).toLocaleString()} hint="Filtered invoice-backed purchase rows" icon={ReceiptText} tone="blue" />
        <SummaryCard title="Net Amount" value={money(summary.totalAmount || 0)} hint="Subtotal before GST" icon={Wallet} tone="orange" />
        <SummaryCard title="GST" value={money(summary.totalGst || 0)} hint="Tax collected over the current filter window" icon={CreditCard} tone="emerald" />
        <SummaryCard title="Invoice Value" value={money(summary.totalInvoiceValue || 0)} hint={`${Number(summary.uniqueCustomers || 0).toLocaleString()} customers across ${Number(summary.services || 0).toLocaleString()} services`} icon={Filter} tone="violet" />
      </div>

      <Card className="border-slate-200 shadow-sm">
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <Filter className="h-5 w-5 text-blue-600" />
            Filters
          </CardTitle>
          <CardDescription>Date and service filters are applied in SQL with paged results. Leave dates blank to review the full purchase history.</CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-6">
            <div className="space-y-2">
              <Label>From Date</Label>
              <Input type="date" value={filters.fromDate} onChange={(event) => setFilters((prev) => ({ ...prev, fromDate: event.target.value }))} />
            </div>
            <div className="space-y-2">
              <Label>To Date</Label>
              <Input type="date" value={filters.toDate} onChange={(event) => setFilters((prev) => ({ ...prev, toDate: event.target.value }))} />
            </div>
            <div className="space-y-2">
              <Label>Service</Label>
              <Select value={filters.service || "all"} onValueChange={(value) => setFilters((prev) => ({ ...prev, service: value === "all" ? "" : value }))}>
                <SelectTrigger><SelectValue placeholder="All services" /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">All services</SelectItem>
                  {serviceOptions.map((option) => (
                    <SelectItem key={`${option.code || option.name}`} value={option.code || option.name}>
                      {option.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-2">
              <Label>Status</Label>
              <Select value={filters.status} onValueChange={(value) => setFilters((prev) => ({ ...prev, status: value }))}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">All statuses</SelectItem>
                  <SelectItem value="paid">Paid</SelectItem>
                  <SelectItem value="issued">Issued</SelectItem>
                  <SelectItem value="refunded">Refunded</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-2">
              <Label>Rows Per Page</Label>
              <Select value={String(filters.pageSize)} onValueChange={(value) => setFilters((prev) => ({ ...prev, pageSize: Number(value) }))}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="25">25</SelectItem>
                  <SelectItem value="50">50</SelectItem>
                  <SelectItem value="100">100</SelectItem>
                  <SelectItem value="200">200</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-2">
              <Label>Search</Label>
              <div className="relative">
                <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
                <Input
                  value={filters.q}
                  onChange={(event) => setFilters((prev) => ({ ...prev, q: event.target.value }))}
                  onKeyDown={(event) => {
                    if (event.key === "Enter") applyFilters();
                  }}
                  className="pl-9"
                  placeholder="User, company, GST, invoice"
                />
              </div>
            </div>
          </div>
          <div className="flex flex-wrap gap-3">
            <Button className="bg-blue-600 text-white hover:bg-blue-700" onClick={() => applyFilters()} disabled={refreshing || loading}>
              Apply Filters
            </Button>
            <Button
              variant="outline"
              onClick={() => {
                const reset = { fromDate: defaultFrom, toDate: defaultTo, service: "", status: "all", q: "", page: 1, pageSize: 50 };
                setFilters(reset);
                load(reset);
              }}
            >
              Reset
            </Button>
          </div>
        </CardContent>
      </Card>

      <Card className="border-slate-200 shadow-sm">
        <CardHeader>
          <div className="flex flex-col gap-3 lg:flex-row lg:items-end lg:justify-between">
            <div>
              <CardTitle>Purchase ledger</CardTitle>
              <CardDescription>Paged owner view of invoice-backed purchases with direct invoice view and resend actions.</CardDescription>
            </div>
            <div className="text-sm text-slate-500">
              {loading
                ? "Loading report..."
                : `Page ${Number(report.page || 1).toLocaleString()} of ${Number(report.totalPages || 1).toLocaleString()} • ${Number(report.totalCount || 0).toLocaleString()} rows`}
            </div>
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="overflow-hidden rounded-2xl border border-slate-200 bg-white">
            <div className="overflow-x-auto">
              <table className="w-full min-w-[1360px] text-sm">
                <thead className="bg-slate-50">
                  <tr>
                    {["S.No", "User", "Company", "GST No", "Purchase Date", "Service", "Amount", "GST", "Invoice Date", "Invoice", "Actions"].map((header) => (
                      <th key={header} className="px-4 py-3 text-left font-semibold text-slate-600">{header}</th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {rows.map((row) => (
                    <tr key={row.invoiceId} className="border-t border-slate-100 align-top">
                      <td className="px-4 py-3 font-medium text-slate-900">{row.sNo}</td>
                      <td className="px-4 py-3">
                        <p className="font-medium text-slate-950">{row.userName || "-"}</p>
                        <p className="text-xs text-slate-500">{row.userEmail || row.billingEmail || "-"}</p>
                      </td>
                      <td className="px-4 py-3">
                        <p className="font-medium text-slate-950">{row.companyName || "-"}</p>
                        <p className="text-xs text-slate-500">{row.billingCycle || "-"}</p>
                      </td>
                      <td className="px-4 py-3 text-slate-700">{row.gstNo || "-"}</td>
                      <td className="px-4 py-3 text-slate-700">{fmtDate(row.purchaseDateUtc)}</td>
                      <td className="px-4 py-3">
                        <p className="font-medium text-slate-950">{row.serviceName || "-"}</p>
                        <p className="text-xs text-slate-500">{row.serviceCode || row.invoiceKind || "-"}</p>
                      </td>
                      <td className="px-4 py-3 text-slate-900">{money(row.amount, row.currency)}</td>
                      <td className="px-4 py-3 text-slate-900">{money(row.gstAmount, row.currency)}</td>
                      <td className="px-4 py-3 text-slate-700">{fmtDate(row.invoiceDateUtc)}</td>
                      <td className="px-4 py-3">
                        <p className="font-medium text-slate-950">{row.invoiceNo || "-"}</p>
                        <div className="mt-2">
                          <StatusBadge status={row.invoiceStatus} />
                        </div>
                      </td>
                      <td className="px-4 py-3">
                        <div className="flex flex-wrap gap-2">
                          <Button variant="outline" size="sm" className="gap-1" onClick={() => handleViewInvoice(row)}>
                            <Eye className="h-4 w-4" />
                            View
                          </Button>
                          <Button
                            variant="outline"
                            size="sm"
                            className="gap-1"
                            onClick={() => handleResendInvoice(row)}
                            disabled={sendingInvoiceId === row.invoiceId}
                          >
                            <Mail className="h-4 w-4" />
                            {sendingInvoiceId === row.invoiceId ? "Sending..." : "Resend"}
                          </Button>
                        </div>
                      </td>
                    </tr>
                  ))}
                  {!rows.length ? (
                    <tr>
                      <td colSpan={11} className="px-4 py-12 text-center text-slate-500">
                        {loading ? "Loading purchase report..." : "No purchases match the selected filters."}
                      </td>
                    </tr>
                  ) : null}
                </tbody>
              </table>
            </div>
          </div>

          <div className="flex flex-col gap-3 rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3 md:flex-row md:items-center md:justify-between">
            <p className="text-sm text-slate-600">
              Showing page {Number(report.page || 1).toLocaleString()} of {Number(report.totalPages || 1).toLocaleString()} with {Number(report.totalCount || 0).toLocaleString()} total rows.
            </p>
            <div className="flex gap-2">
              <Button variant="outline" size="sm" onClick={() => changePage((report.page || 1) - 1)} disabled={!report.hasPreviousPage || refreshing}>
                <ChevronLeft className="mr-1 h-4 w-4" />
                Previous
              </Button>
              <Button variant="outline" size="sm" onClick={() => changePage((report.page || 1) + 1)} disabled={!report.hasNextPage || refreshing}>
                Next
                <ChevronRight className="ml-1 h-4 w-4" />
              </Button>
            </div>
          </div>
        </CardContent>
      </Card>
    </div>
  );
}
