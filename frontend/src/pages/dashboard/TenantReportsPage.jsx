import { useEffect, useMemo, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { toast } from "sonner";
import { downloadBillingInvoice, getBillingInvoices, listSmsBillingLedger, listWhatsappMessageReport } from "@/lib/api";
import KycReportsPage from "./KycReportsPage";
import TenantCreditLedgerReportPage from "./TenantCreditLedgerReportPage";
import TenantLedgerReportPage from "./TenantLedgerReportPage";

const REPORT_OPTIONS = [
  { value: "ledger", label: "Unified Ledger", description: "One timeline for SMS, WhatsApp, KYC, and billing purchases." },
  { value: "credit-ledger", label: "Credit Ledger", description: "Simple debit, refund, and credit movement trail for credit-based services." },
  { value: "sms", label: "SMS Usage Report", description: "Per-message SMS billing and delivery ledger." },
  { value: "whatsapp", label: "WhatsApp Usage Report", description: "Outbound WhatsApp messages with status and delivery timing." },
  { value: "kyc", label: "KYC Usage Report", description: "KYC sessions with document previews, status, and extracted data." },
  { value: "purchases", label: "Purchase Report", description: "Invoices, payments, and subscription purchase history." },
];

const formatUtc = (value) => {
  if (!value) return "-";
  try {
    return new Date(value).toLocaleString();
  } catch {
    return String(value || "-");
  }
};

const normalizeStatus = (raw) => {
  const value = String(raw || "").trim();
  const lower = value.toLowerCase();
  if (!value) return { label: "-", variant: "secondary" };
  if (["delivered", "success", "paid", "verified", "submitted"].includes(lower)) return { label: value, variant: "default" };
  if (lower.includes("fail") || lower.includes("error") || lower.includes("reject")) return { label: value, variant: "destructive" };
  if (lower.includes("pending") || lower.includes("queued") || lower.includes("processing")) return { label: value, variant: "secondary" };
  return { label: value, variant: "secondary" };
};

const money = (value, currency = "INR") => `${String(currency || "INR").toUpperCase()} ${Number(value || 0).toLocaleString("en-IN", { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;

const isWithinDateRange = (value, fromDate, toDate) => {
  if (!value) return true;
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return true;
  if (fromDate && date < new Date(`${fromDate}T00:00:00`)) return false;
  if (toDate && date > new Date(`${toDate}T23:59:59.999`)) return false;
  return true;
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

function SmsUsageReportPanel() {
  const [rows, setRows] = useState([]);
  const [busy, setBusy] = useState(false);
  const [query, setQuery] = useState("");
  const [fromDate, setFromDate] = useState("");
  const [toDate, setToDate] = useState("");
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(null);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);

  const refresh = async () => {
    setBusy(true);
    try {
      const data = await listSmsBillingLedger(300);
      setRows(Array.isArray(data) ? data : []);
    } catch (e) {
      toast.error(e?.message || "Failed to load SMS report");
    } finally {
      setBusy(false);
    }
  };

  useEffect(() => {
    refresh();
  }, []);

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    return rows.filter((row) => {
      if (!isWithinDateRange(row?.createdAtUtc, fromDate, toDate)) return false;
      if (!q) return true;
      const recipient = String(row?.recipient || "").toLowerCase();
      const provider = String(row?.providerMessageId || "").toLowerCase();
      const messageId = String(row?.messageId || "").toLowerCase();
      return recipient.includes(q) || provider.includes(q) || messageId.includes(q);
    });
  }, [fromDate, query, rows, toDate]);

  const totalPages = useMemo(() => Math.max(1, Math.ceil(filtered.length / pageSize)), [filtered.length, pageSize]);
  const paged = useMemo(() => filtered.slice((page - 1) * pageSize, page * pageSize), [filtered, page, pageSize]);

  useEffect(() => {
    setPage((prev) => Math.min(prev, totalPages));
  }, [totalPages]);

  const exportRows = () => {
    downloadCsv("sms-usage-report.csv", [
      ["Date", "Ref ID", "Recipient", "Segments", "Billing State", "Delivery State", "Amount", "Provider Message ID"],
      ...filtered.map((row) => [
        formatUtc(row?.createdAtUtc),
        row?.messageId || row?.id || "",
        row?.recipient || "",
        row?.segments || 0,
        row?.billingState || "",
        row?.deliveryState || "",
        money(row?.totalAmount || 0, row?.currency || "INR"),
        row?.providerMessageId || "",
      ]),
    ]);
    toast.success("SMS report exported");
  };

  return (
    <Card className="border-slate-200">
      <CardHeader className="flex flex-col gap-4 md:flex-row md:items-start md:justify-between">
        <div>
          <CardTitle>SMS Usage Report</CardTitle>
          <CardDescription>Per-message billing ledger with delivery status and provider references.</CardDescription>
        </div>
        <div className="flex flex-wrap gap-2">
          <Input value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Search recipient / ref id" className="h-10 w-56" />
          <Input type="date" value={fromDate} onChange={(e) => setFromDate(e.target.value)} className="h-10 w-40" />
          <Input type="date" value={toDate} onChange={(e) => setToDate(e.target.value)} className="h-10 w-40" />
          <Button variant="outline" onClick={exportRows} disabled={!filtered.length}>Export CSV</Button>
          <Button className="bg-orange-500 hover:bg-orange-600" disabled={busy} onClick={refresh}>
            {busy ? "Loading..." : "Refresh"}
          </Button>
        </div>
      </CardHeader>
      <CardContent>
        <div className="overflow-auto rounded-2xl border border-slate-200">
          <table className="min-w-full text-left text-sm">
            <thead className="sticky top-0 z-10 bg-slate-50 text-slate-600 shadow-sm">
              <tr>
                <th className="px-4 py-3">S.No</th>
                <th className="px-4 py-3">Date</th>
                <th className="px-4 py-3">Ref ID</th>
                <th className="px-4 py-3">Recipient</th>
                <th className="px-4 py-3">Type</th>
                <th className="px-4 py-3">Status</th>
                <th className="px-4 py-3 text-right">View</th>
              </tr>
            </thead>
            <tbody>
              {paged.length === 0 ? (
                <tr>
                  <td colSpan={7} className="px-4 py-10 text-center text-slate-500">No SMS records yet.</td>
                </tr>
              ) : (
                paged.map((row, idx) => {
                  const status = normalizeStatus(row?.deliveryState || row?.billingState);
                  return (
                    <tr key={row?.id || idx} className="border-t border-slate-200">
                      <td className="px-4 py-3">{(page - 1) * pageSize + idx + 1}</td>
                      <td className="px-4 py-3 text-slate-500">{formatUtc(row?.createdAtUtc)}</td>
                      <td className="px-4 py-3 font-medium text-slate-900">{String(row?.messageId || row?.id || "-")}</td>
                      <td className="px-4 py-3">{row?.recipient || "-"}</td>
                      <td className="px-4 py-3">{row?.segments ? `${row.segments} segment(s)` : "-"}</td>
                      <td className="px-4 py-3"><Badge variant={status.variant}>{status.label}</Badge></td>
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
          <div className="text-sm text-slate-600">Showing {filtered.length ? ((page - 1) * pageSize) + 1 : 0}�{Math.min(page * pageSize, filtered.length)} of {filtered.length.toLocaleString()} rows</div>
          <div className="flex items-center gap-2">
            <Select value={String(pageSize)} onValueChange={(value) => { setPageSize(Number(value)); setPage(1); }}>
              <SelectTrigger className="h-9 w-[110px]"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="10">10 / page</SelectItem>
                <SelectItem value="25">25 / page</SelectItem>
                <SelectItem value="50">50 / page</SelectItem>
              </SelectContent>
            </Select>
            <Button variant="outline" className="rounded-xl" disabled={page <= 1} onClick={() => setPage((prev) => prev - 1)}>Previous</Button>
            <div className="min-w-[90px] text-center text-sm text-slate-600">Page {page} / {totalPages}</div>
            <Button variant="outline" className="rounded-xl" disabled={page >= totalPages} onClick={() => setPage((prev) => prev + 1)}>Next</Button>
          </div>
        </div>
      </CardContent>

      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent className="max-w-3xl">
          <DialogHeader>
            <DialogTitle>SMS Report Detail</DialogTitle>
          </DialogHeader>
          {active ? (
            <div className="space-y-3 text-sm">
              <div className="grid grid-cols-2 gap-3 rounded-2xl border border-slate-200 bg-slate-50 p-4">
                <div>
                  <div className="text-slate-500">Recipient</div>
                  <div className="font-medium text-slate-900">{active.recipient || "-"}</div>
                </div>
                <div>
                  <div className="text-slate-500">Provider Message ID</div>
                  <div className="font-medium text-slate-900">{active.providerMessageId || "-"}</div>
                </div>
                <div>
                  <div className="text-slate-500">Billing</div>
                  <div className="font-medium text-slate-900">{active.currency || "INR"} {Number(active.totalAmount || 0).toFixed(2)}</div>
                </div>
                <div>
                  <div className="text-slate-500">Segments</div>
                  <div className="font-medium text-slate-900">{active.segments || 0}</div>
                </div>
                <div>
                  <div className="text-slate-500">Delivery State</div>
                  <div className="font-medium text-slate-900">{active.deliveryState || "-"}</div>
                </div>
                <div>
                  <div className="text-slate-500">Updated</div>
                  <div className="font-medium text-slate-900">{formatUtc(active.updatedAtUtc)}</div>
                </div>
              </div>
              <div className="rounded-2xl border border-slate-200 bg-white p-3">
                <div className="text-xs font-medium text-slate-500">Raw record</div>
                <pre className="mt-2 max-h-64 overflow-auto rounded-xl bg-slate-50 p-3 text-xs text-slate-700">{JSON.stringify(active, null, 2)}</pre>
              </div>
            </div>
          ) : null}
        </DialogContent>
      </Dialog>
    </Card>
  );
}

function WhatsAppUsageReportPanel() {
  const [rows, setRows] = useState([]);
  const [busy, setBusy] = useState(false);
  const [query, setQuery] = useState("");
  const [fromDate, setFromDate] = useState("");
  const [toDate, setToDate] = useState("");
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(null);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);

  const refresh = async () => {
    setBusy(true);
    try {
      const data = await listWhatsappMessageReport(300);
      setRows(Array.isArray(data) ? data : []);
    } catch (e) {
      toast.error(e?.message || "Failed to load WhatsApp report");
    } finally {
      setBusy(false);
    }
  };

  useEffect(() => {
    refresh();
  }, []);

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    return rows.filter((row) => {
      if (!isWithinDateRange(row?.createdAtUtc, fromDate, toDate)) return false;
      if (!q) return true;
      const recipient = String(row?.recipient || "").toLowerCase();
      const status = String(row?.status || "").toLowerCase();
      const provider = String(row?.providerMessageId || "").toLowerCase();
      const id = String(row?.id || "").toLowerCase();
      return recipient.includes(q) || status.includes(q) || provider.includes(q) || id.includes(q);
    });
  }, [fromDate, query, rows, toDate]);

  const totalPages = useMemo(() => Math.max(1, Math.ceil(filtered.length / pageSize)), [filtered.length, pageSize]);
  const paged = useMemo(() => filtered.slice((page - 1) * pageSize, page * pageSize), [filtered, page, pageSize]);

  useEffect(() => {
    setPage((prev) => Math.min(prev, totalPages));
  }, [totalPages]);

  const exportRows = () => {
    downloadCsv("whatsapp-usage-report.csv", [
      ["Date", "Ref ID", "Recipient", "Type", "Status", "Provider Message ID", "Delivered", "Read", "Last Error"],
      ...filtered.map((row) => [
        formatUtc(row?.createdAtUtc),
        row?.id || "",
        row?.recipient || "",
        row?.messageType || "",
        row?.status || "",
        row?.providerMessageId || "",
        formatUtc(row?.deliveredAtUtc),
        formatUtc(row?.readAtUtc),
        row?.lastError || "",
      ]),
    ]);
    toast.success("WhatsApp report exported");
  };

  return (
    <Card className="border-slate-200">
      <CardHeader className="flex flex-col gap-4 md:flex-row md:items-start md:justify-between">
        <div>
          <CardTitle>WhatsApp Usage Report</CardTitle>
          <CardDescription>Outbound WhatsApp messages with status, provider ID, and delivery timing.</CardDescription>
        </div>
        <div className="flex flex-wrap gap-2">
          <Input value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Search recipient / status / ref id" className="h-10 w-56" />
          <Input type="date" value={fromDate} onChange={(e) => setFromDate(e.target.value)} className="h-10 w-40" />
          <Input type="date" value={toDate} onChange={(e) => setToDate(e.target.value)} className="h-10 w-40" />
          <Button variant="outline" onClick={exportRows} disabled={!filtered.length}>Export CSV</Button>
          <Button className="bg-orange-500 hover:bg-orange-600" disabled={busy} onClick={refresh}>
            {busy ? "Loading..." : "Refresh"}
          </Button>
        </div>
      </CardHeader>
      <CardContent>
        <div className="overflow-auto rounded-2xl border border-slate-200">
          <table className="min-w-full text-left text-sm">
            <thead className="sticky top-0 z-10 bg-slate-50 text-slate-600 shadow-sm">
              <tr>
                <th className="px-4 py-3">S.No</th>
                <th className="px-4 py-3">Date</th>
                <th className="px-4 py-3">Ref ID</th>
                <th className="px-4 py-3">Recipient</th>
                <th className="px-4 py-3">Type</th>
                <th className="px-4 py-3">Status</th>
                <th className="px-4 py-3 text-right">View</th>
              </tr>
            </thead>
            <tbody>
              {paged.length === 0 ? (
                <tr>
                  <td colSpan={7} className="px-4 py-10 text-center text-slate-500">No WhatsApp records yet.</td>
                </tr>
              ) : (
                paged.map((row, idx) => {
                  const status = normalizeStatus(row?.status);
                  return (
                    <tr key={row?.id || idx} className="border-t border-slate-200">
                      <td className="px-4 py-3">{(page - 1) * pageSize + idx + 1}</td>
                      <td className="px-4 py-3 text-slate-500">{formatUtc(row?.createdAtUtc)}</td>
                      <td className="px-4 py-3 font-medium text-slate-900">{String(row?.id || "-")}</td>
                      <td className="px-4 py-3">{row?.recipient || "-"}</td>
                      <td className="px-4 py-3">{row?.messageType || "-"}</td>
                      <td className="px-4 py-3"><Badge variant={status.variant}>{status.label}</Badge></td>
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
          <div className="text-sm text-slate-600">Showing {filtered.length ? ((page - 1) * pageSize) + 1 : 0}�{Math.min(page * pageSize, filtered.length)} of {filtered.length.toLocaleString()} rows</div>
          <div className="flex items-center gap-2">
            <Select value={String(pageSize)} onValueChange={(value) => { setPageSize(Number(value)); setPage(1); }}>
              <SelectTrigger className="h-9 w-[110px]"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="10">10 / page</SelectItem>
                <SelectItem value="25">25 / page</SelectItem>
                <SelectItem value="50">50 / page</SelectItem>
              </SelectContent>
            </Select>
            <Button variant="outline" className="rounded-xl" disabled={page <= 1} onClick={() => setPage((prev) => prev - 1)}>Previous</Button>
            <div className="min-w-[90px] text-center text-sm text-slate-600">Page {page} / {totalPages}</div>
            <Button variant="outline" className="rounded-xl" disabled={page >= totalPages} onClick={() => setPage((prev) => prev + 1)}>Next</Button>
          </div>
        </div>
      </CardContent>

      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent className="max-w-3xl">
          <DialogHeader>
            <DialogTitle>WhatsApp Report Detail</DialogTitle>
          </DialogHeader>
          {active ? (
            <div className="space-y-3 text-sm">
              <div className="grid grid-cols-2 gap-3 rounded-2xl border border-slate-200 bg-slate-50 p-4">
                <div>
                  <div className="text-slate-500">Recipient</div>
                  <div className="font-medium text-slate-900">{active.recipient || "-"}</div>
                </div>
                <div>
                  <div className="text-slate-500">Provider Message ID</div>
                  <div className="font-medium text-slate-900">{active.providerMessageId || "-"}</div>
                </div>
                <div>
                  <div className="text-slate-500">Status</div>
                  <div className="font-medium text-slate-900">{active.status || "-"}</div>
                </div>
                <div>
                  <div className="text-slate-500">Delivered</div>
                  <div className="font-medium text-slate-900">{formatUtc(active.deliveredAtUtc)}</div>
                </div>
                <div>
                  <div className="text-slate-500">Read</div>
                  <div className="font-medium text-slate-900">{formatUtc(active.readAtUtc)}</div>
                </div>
                <div>
                  <div className="text-slate-500">Last Error</div>
                  <div className="font-medium text-slate-900">{active.lastError || "-"}</div>
                </div>
              </div>
              <div className="rounded-2xl border border-slate-200 bg-white p-3">
                <div className="text-xs font-medium text-slate-500">Message body</div>
                <div className="mt-2 rounded-xl bg-slate-50 p-3 text-xs text-slate-700 whitespace-pre-wrap">
                  {active.body || "-"}
                </div>
              </div>
            </div>
          ) : null}
        </DialogContent>
      </Dialog>
    </Card>
  );
}

function PurchaseReportPanel() {
  const [rows, setRows] = useState([]);
  const [busy, setBusy] = useState(false);
  const [query, setQuery] = useState("");
  const [fromDate, setFromDate] = useState("");
  const [toDate, setToDate] = useState("");
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);

  const refresh = async () => {
    setBusy(true);
    try {
      const data = await getBillingInvoices();
      setRows(Array.isArray(data) ? data : []);
    } catch (e) {
      toast.error(e?.message || "Failed to load purchase report");
    } finally {
      setBusy(false);
    }
  };

  useEffect(() => {
    refresh();
  }, []);

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    return rows.filter((row) => {
      if (!isWithinDateRange(row?.issuedAtUtc || row?.createdAtUtc, fromDate, toDate)) return false;
      if (!q) return true;
      const invoiceNo = String(row?.invoiceNo || "").toLowerCase();
      const reference = String(row?.referenceNo || "").toLowerCase();
      const status = String(row?.status || "").toLowerCase();
      return invoiceNo.includes(q) || reference.includes(q) || status.includes(q);
    });
  }, [fromDate, query, rows, toDate]);

  const totalPages = useMemo(() => Math.max(1, Math.ceil(filtered.length / pageSize)), [filtered.length, pageSize]);
  const paged = useMemo(() => filtered.slice((page - 1) * pageSize, page * pageSize), [filtered, page, pageSize]);

  useEffect(() => {
    setPage((prev) => Math.min(prev, totalPages));
  }, [totalPages]);

  const exportRows = () => {
    downloadCsv("purchase-report.csv", [
      ["Date", "Ref ID", "Invoice No", "Type", "Status", "Subtotal", "Tax", "Total"],
      ...filtered.map((row) => [
        formatUtc(row?.issuedAtUtc || row?.createdAtUtc),
        row?.referenceNo || row?.invoiceNo || row?.id || "",
        row?.invoiceNo || "",
        row?.invoiceKind || "",
        row?.status || "",
        money(row?.subtotal || 0),
        money(row?.taxAmount || 0),
        money(row?.total || 0),
      ]),
    ]);
    toast.success("Purchase report exported");
  };

  const download = async (row) => {
    try {
      const blob = await downloadBillingInvoice(row.id);
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = `${row.invoiceNo || row.id}.html`;
      a.click();
      URL.revokeObjectURL(url);
    } catch (e) {
      toast.error(e?.message || "Failed to download invoice");
    }
  };

  return (
    <Card className="border-slate-200">
      <CardHeader className="flex flex-col gap-4 md:flex-row md:items-start md:justify-between">
        <div>
          <CardTitle>Purchase Report</CardTitle>
          <CardDescription>Invoices and subscription payments for your workspace.</CardDescription>
        </div>
        <div className="flex flex-wrap gap-2">
          <Input value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Search invoice / reference / status" className="h-10 w-64" />
          <Input type="date" value={fromDate} onChange={(e) => setFromDate(e.target.value)} className="h-10 w-40" />
          <Input type="date" value={toDate} onChange={(e) => setToDate(e.target.value)} className="h-10 w-40" />
          <Button variant="outline" onClick={exportRows} disabled={!filtered.length}>Export CSV</Button>
          <Button className="bg-orange-500 hover:bg-orange-600" disabled={busy} onClick={refresh}>
            {busy ? "Loading..." : "Refresh"}
          </Button>
        </div>
      </CardHeader>
      <CardContent>
        <div className="overflow-auto rounded-2xl border border-slate-200">
          <table className="min-w-full text-left text-sm">
            <thead className="sticky top-0 z-10 bg-slate-50 text-slate-600 shadow-sm">
              <tr>
                <th className="px-4 py-3">S.No</th>
                <th className="px-4 py-3">Date</th>
                <th className="px-4 py-3">Ref ID</th>
                <th className="px-4 py-3">Type</th>
                <th className="px-4 py-3">Status</th>
                <th className="px-4 py-3">Total</th>
                <th className="px-4 py-3 text-right">View</th>
              </tr>
            </thead>
            <tbody>
              {paged.length === 0 ? (
                <tr>
                  <td colSpan={7} className="px-4 py-10 text-center text-slate-500">No invoices yet.</td>
                </tr>
              ) : (
                paged.map((row, idx) => {
                  const status = normalizeStatus(row?.status);
                  const refId = row?.referenceNo || row?.invoiceNo || row?.id;
                  return (
                    <tr key={row?.id || idx} className="border-t border-slate-200">
                      <td className="px-4 py-3">{(page - 1) * pageSize + idx + 1}</td>
                      <td className="px-4 py-3 text-slate-500">{formatUtc(row?.issuedAtUtc || row?.createdAtUtc)}</td>
                      <td className="px-4 py-3 font-medium text-slate-900">{refId || "-"}</td>
                      <td className="px-4 py-3">{row?.invoiceKind || "-"}</td>
                      <td className="px-4 py-3"><Badge variant={status.variant}>{status.label}</Badge></td>
                      <td className="px-4 py-3">{row?.total ? `INR ${Number(row.total).toFixed(2)}` : "-"}</td>
                      <td className="px-4 py-3 text-right">
                        <Button variant="outline" className="rounded-xl" onClick={() => download(row)}>
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
          <div className="text-sm text-slate-600">Showing {filtered.length ? ((page - 1) * pageSize) + 1 : 0}�{Math.min(page * pageSize, filtered.length)} of {filtered.length.toLocaleString()} rows</div>
          <div className="flex items-center gap-2">
            <Select value={String(pageSize)} onValueChange={(value) => { setPageSize(Number(value)); setPage(1); }}>
              <SelectTrigger className="h-9 w-[110px]"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="10">10 / page</SelectItem>
                <SelectItem value="25">25 / page</SelectItem>
                <SelectItem value="50">50 / page</SelectItem>
              </SelectContent>
            </Select>
            <Button variant="outline" className="rounded-xl" disabled={page <= 1} onClick={() => setPage((prev) => prev - 1)}>Previous</Button>
            <div className="min-w-[90px] text-center text-sm text-slate-600">Page {page} / {totalPages}</div>
            <Button variant="outline" className="rounded-xl" disabled={page >= totalPages} onClick={() => setPage((prev) => prev + 1)}>Next</Button>
          </div>
        </div>
      </CardContent>
    </Card>
  );
}

export default function TenantReportsPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const report = searchParams.get("report") || "sms";
  const selected = useMemo(
    () => REPORT_OPTIONS.find((option) => option.value === report) || REPORT_OPTIONS[0],
    [report],
  );

  return (
    <div className="space-y-4" data-testid="tenant-reports-page">
      <div>
        <h1 className="text-2xl font-semibold text-slate-900">Reports</h1>
        <p className="text-sm text-slate-500">Switch between SMS, WhatsApp, KYC, and purchase reports.</p>
      </div>

      <Card className="border-slate-200">
        <CardHeader>
          <CardTitle>Report Selector</CardTitle>
          <CardDescription>{selected?.description}</CardDescription>
        </CardHeader>
        <CardContent className="grid gap-4 md:grid-cols-2">
          <div className="space-y-2">
            <Label>Report</Label>
            <Select value={report} onValueChange={(value) => setSearchParams({ report: value })}>
              <SelectTrigger>
                <SelectValue placeholder="Select report" />
              </SelectTrigger>
              <SelectContent>
                {REPORT_OPTIONS.map((option) => (
                  <SelectItem key={option.value} value={option.value}>
                    {option.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <p className="text-xs text-slate-500">Each report includes its own search and view drill-down.</p>
          </div>
        </CardContent>
      </Card>

      {report === "ledger" && <TenantLedgerReportPage />}
      {report === "credit-ledger" && <TenantCreditLedgerReportPage />}
      {report === "sms" && <SmsUsageReportPanel />}
      {report === "whatsapp" && <WhatsAppUsageReportPanel />}
      {report === "kyc" && <KycReportsPage />}
      {report === "purchases" && <PurchaseReportPanel />}
    </div>
  );
}

