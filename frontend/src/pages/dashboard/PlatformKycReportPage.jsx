import { useEffect, useMemo, useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Calendar } from "@/components/ui/calendar";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover";
import {
  Building2,
  Calendar as CalendarIcon,
  CheckCircle2,
  Clock3,
  Copy,
  FileJson2,
  FileText,
  ImageIcon,
  ShieldCheck,
  XCircle,
} from "lucide-react";
import { format } from "date-fns";
import { toast } from "sonner";
import {
  getPlatformCustomerUsage,
  getPlatformCustomers,
  getPlatformKycReport,
  getPlatformKycReportSession,
} from "@/lib/api";

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

const formatDate = (value) => {
  if (!value) return "-";
  try {
    return new Date(value).toLocaleString();
  } catch {
    return String(value);
  }
};

const statusBadge = (status) => {
  const normalized = String(status || "").toLowerCase();
  if (normalized === "verified" || normalized === "success") return { label: "Success", className: "border border-emerald-200 bg-emerald-100 text-emerald-700 hover:bg-emerald-100", icon: CheckCircle2 };
  if (normalized === "failed" || normalized === "error") return { label: "Fail", className: "border border-rose-200 bg-rose-100 text-rose-700 hover:bg-rose-100", icon: XCircle };
  if (normalized === "created" || normalized === "redirected" || normalized === "pending" || normalized === "expired") return { label: "Pending", className: "border border-amber-200 bg-amber-100 text-amber-700 hover:bg-amber-100", icon: Clock3 };
  return { label: normalized || "-", className: "border border-slate-200 bg-slate-100 text-slate-700 hover:bg-slate-100", icon: Clock3 };
};

function decodeBase64ToBlobUrl(base64, mime = "application/pdf") {
  const b = String(base64 || "").trim();
  if (!b) return "";
  const binary = atob(b);
  const bytes = new Uint8Array(binary.length);
  for (let index = 0; index < binary.length; index += 1) bytes[index] = binary.charCodeAt(index);
  return URL.createObjectURL(new Blob([bytes], { type: mime || "application/octet-stream" }));
}

function toDataUrl(base64) {
  const b = String(base64 || "").trim();
  if (!b) return "";
  return `data:image/jpeg;base64,${b}`;
}

function safeGet(obj, key, fallback = "") {
  try {
    if (!obj) return fallback;
    const value = obj[key];
    return value === null || value === undefined ? fallback : value;
  } catch {
    return fallback;
  }
}

function formatApiName(provider) {
  const normalized = String(provider || "").trim().toLowerCase();
  if (normalized === "gst") return "AppyFlow GST";
  if (normalized === "digilocker") return "DigiLocker";
  return normalized ? normalized.toUpperCase() : "-";
}

function apiMeta(provider) {
  const normalized = String(provider || "").trim().toLowerCase();
  if (normalized === "gst") return { label: "AppyFlow GST", className: "border border-cyan-200 bg-cyan-100 text-cyan-700 hover:bg-cyan-100", accent: "from-cyan-50 to-sky-50", icon: Building2 };
  if (normalized === "digilocker") return { label: "DigiLocker", className: "border border-emerald-200 bg-emerald-100 text-emerald-700 hover:bg-emerald-100", accent: "from-emerald-50 to-lime-50", icon: ShieldCheck };
  return { label: formatApiName(provider), className: "border border-slate-200 bg-slate-100 text-slate-700 hover:bg-slate-100", accent: "from-slate-50 to-white", icon: FileJson2 };
}

function formatResponse(value) {
  if (!value) return "{}";
  if (typeof value === "string") {
    try {
      return JSON.stringify(JSON.parse(value), null, 2);
    } catch {
      return value;
    }
  }
  try {
    return JSON.stringify(value, null, 2);
  } catch {
    return String(value);
  }
}

function prettifyFailureReason(reason) {
  const text = String(reason || "").trim();
  if (!text) return "";
  const normalized = text.toLowerCase();
  if (normalized.includes("invalid_grant_type") || normalized.includes("disable openid")) {
    return "DigiLocker token exchange failed because openid is still enabled in the DigiLocker client scopes. Disable openid in the DigiLocker client configuration and retry the session.";
  }
  if (normalized.startsWith("provider_error:")) {
    return text.slice("provider_error:".length).trim() || "Provider returned an error.";
  }
  return text;
}

function KycPreviewCard({ collected, active }) {
  const name = safeGet(collected, "name", "-");
  const dob = safeGet(collected, "dob", "-");
  const gender = safeGet(collected, "gender", "-");
  const fatherName = safeGet(collected, "fatherName", "-");
  const aadhaar = safeGet(collected, "aadhaarNumber", "") || safeGet(collected, "aadhaarMasked", "") || (String(safeGet(collected, "aadhaarVerified", "")).toLowerCase() === "true" ? "Verified" : "-");
  const pan = safeGet(collected, "pan", "-");
  const address = safeGet(collected, "address", "-");
  const photo = toDataUrl(collected.photoBase64);
  const docType = Array.isArray(active?.docTypes) && active.docTypes[0] ? String(active.docTypes[0]).toUpperCase() : "KYC";

  return (
    <div className="relative h-[420px] w-full overflow-hidden rounded-xl bg-white p-5">
      <div className="absolute inset-0 pointer-events-none flex items-center justify-center opacity-[0.06]">
        <div className="text-5xl font-black tracking-tight text-slate-900">Textzy</div>
      </div>
      <div className="relative">
        <div className="flex items-center justify-between">
          <div>
            <div className="text-sm font-semibold text-slate-900">DigiLocker Document Preview</div>
            <div className="text-xs text-slate-500">{docType} KYC Record</div>
          </div>
          <Badge className="bg-emerald-600 hover:bg-emerald-600">Verified</Badge>
        </div>
        <div className="mt-4 grid grid-cols-[1fr_140px] gap-4 rounded-2xl border border-slate-200 p-4">
          <div className="grid grid-cols-2 gap-3 text-xs">
            <div><div className="text-slate-500">Name</div><div className="font-semibold text-slate-900">{name}</div></div>
            <div><div className="text-slate-500">DOB</div><div className="font-semibold text-slate-900">{dob}</div></div>
            <div><div className="text-slate-500">Gender</div><div className="font-semibold text-slate-900">{gender}</div></div>
            <div><div className="text-slate-500">Father Name</div><div className="font-semibold text-slate-900">{fatherName}</div></div>
            <div><div className="text-slate-500">Aadhaar</div><div className="font-semibold text-slate-900">{aadhaar}</div></div>
            <div><div className="text-slate-500">PAN</div><div className="font-semibold text-slate-900">{pan}</div></div>
            <div className="col-span-2"><div className="text-slate-500">Address</div><div className="font-semibold text-slate-900">{address}</div></div>
          </div>
          <div className="flex flex-col items-center gap-2">
            {photo ? <img src={photo} alt="photo" className="h-[150px] w-[120px] rounded-xl border border-slate-200 object-cover" /> : <div className="flex h-[150px] w-[120px] items-center justify-center rounded-xl border border-slate-200 bg-slate-50 text-xs text-slate-400">No photo</div>}
          </div>
        </div>
      </div>
    </div>
  );
}

export default function PlatformKycReportPage() {
  const [rows, setRows] = useState([]);
  const [tenants, setTenants] = useState([]);
  const [loading, setLoading] = useState(false);
  const [filters, setFilters] = useState({ tenantId: "", tenantSlug: "", status: "all", q: "", fromUtc: "", toUtc: "", take: 100, skip: 0 });
  const [useAllTime, setUseAllTime] = useState(true);
  const [fromDate, setFromDate] = useState(null);
  const [toDate, setToDate] = useState(null);
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(null);
  const [activeDetail, setActiveDetail] = useState(null);
  const [previewUrl, setPreviewUrl] = useState("");
  const [activeFileIndex, setActiveFileIndex] = useState(0);
  const [usageSnapshot, setUsageSnapshot] = useState(null);
  const [totalCount, setTotalCount] = useState(0);
  const load = async (patch = {}) => {
    const next = { ...filters, ...patch };
    setFilters(next);
    setLoading(true);
    try {
      const [customerRows, report] = await Promise.all([
        getPlatformCustomers("").catch(() => []),
        getPlatformKycReport({
          tenantId: next.tenantId,
          tenantSlug: next.tenantSlug,
          status: next.status === "all" ? "" : next.status,
          q: next.q,
          fromUtc: useAllTime ? "" : next.fromUtc,
          toUtc: useAllTime ? "" : next.toUtc,
          take: next.take,
          skip: next.skip,
        }),
      ]);
      setTenants(Array.isArray(customerRows) ? customerRows : []);
      setRows(Array.isArray(report?.items) ? report.items : []);
      setTotalCount(Number(report?.total || report?.totalCount || (Array.isArray(report?.items) ? report.items.length : 0)));
      setUsageSnapshot(next.tenantId ? await getPlatformCustomerUsage(next.tenantId).catch(() => null) : null);
    } catch (error) {
      toast.error(error?.message || "Failed to load KYC report");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => () => {
    if (previewUrl) URL.revokeObjectURL(previewUrl);
  }, [previewUrl]);

  const files = useMemo(() => {
    const docs = activeDetail?.result?.documents || active?.result?.documents;
    return Array.isArray(docs) ? docs : [];
  }, [active, activeDetail]);

  const activeFile = files[activeFileIndex] || null;
  const activeProvider = String(activeDetail?.provider || active?.provider || "").toLowerCase();
  const isGstProvider = activeProvider === "gst";
  const publicTabLabel = isGstProvider ? "Formatted AppyFlow Response" : "Formatted Response";
  const rawTabLabel = "Received API Response";
  const activeApi = apiMeta(activeDetail?.provider || active?.provider);

  const summary = useMemo(() => ({
    total: totalCount || rows.length,
    verified: rows.filter((row) => String(row?.status || "").toLowerCase() === "verified").length,
    creditsUsed: rows.reduce((sum, row) => sum + Number(row?.creditsUsed || 0), 0),
  }), [rows, totalCount]);

  const tenantKycUsed = Number(usageSnapshot?.values?.digilockerKyc || 0);
  const tenantKycCredits = Number(usageSnapshot?.creditBalances?.digilockerKyc || 0);

  useEffect(() => {
    if (!activeFile?.fileBase64) {
      setPreviewUrl((prev) => {
        if (prev) URL.revokeObjectURL(prev);
        return "";
      });
      return;
    }
    const url = decodeBase64ToBlobUrl(activeFile.fileBase64, activeFile.mime);
    setPreviewUrl((prev) => {
      if (prev) URL.revokeObjectURL(prev);
      return url;
    });
    return () => {
      if (url) URL.revokeObjectURL(url);
    };
  }, [activeFile?.fileBase64, activeFile?.mime]);

  const copyJson = async (value, label) => {
    try {
      await navigator.clipboard.writeText(formatResponse(value));
      toast.success(`${label} copied`);
    } catch {
      toast.error("Failed to copy response");
    }
  };

  const exportRows = () => {
    downloadCsv("kyc-platform-report.csv", [
      ["Session ID", "Tenant", "User", "API Name", "Customer Ref", "Doc Types", "Status", "Credits Used", "Webhook", "Created At"],
      ...rows.map((row) => [
        row?.sessionId || "",
        row?.tenantName || row?.tenantSlug || "",
        row?.userEmail || "public",
        formatApiName(row?.provider),
        row?.customerRef || "",
        Array.isArray(row?.docTypes) ? row.docTypes.join(" | ") : "",
        statusBadge(row?.status).label,
        Number(row?.creditsUsed || 0),
        row?.webhook ? `${row.webhook.statusCode} (${row.webhook.ok ? "ok" : "failed"})` : "",
        formatDate(row?.createdAtUtc),
      ]),
    ]);
    toast.success("Platform KYC report exported");
  };

  return (
    <div className="space-y-4">
      <Card className="border-slate-200">
        <CardHeader>
          <CardTitle>KYC Platform Report</CardTitle>
          <CardDescription>Every DigiLocker/AppyFlow session with full response, document preview, API name, and webhook status.</CardDescription>
        </CardHeader>
        <CardContent className="grid gap-4 md:grid-cols-4">
          <div className="space-y-2">
            <Label>Tenant</Label>
            <Select value={filters.tenantId || "all"} onValueChange={(value) => {
              if (value === "all") return load({ tenantId: "", tenantSlug: "" });
              const tenant = tenants.find((row) => row.tenantId === value);
              return load({ tenantId: value, tenantSlug: tenant?.tenantSlug || "" });
            }}>
              <SelectTrigger><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="all">All tenants</SelectItem>
                {tenants.map((tenant) => <SelectItem key={tenant.tenantId} value={tenant.tenantId}>{tenant.companyName || tenant.slug || tenant.tenantId}</SelectItem>)}
              </SelectContent>
            </Select>
          </div>
          <div className="space-y-2">
            <Label>Status</Label>
            <Select value={filters.status} onValueChange={(value) => load({ status: value })}>
              <SelectTrigger><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="all">All</SelectItem>
                <SelectItem value="created">Pending</SelectItem>
                <SelectItem value="redirected">Pending redirect</SelectItem>
                <SelectItem value="verified">Success</SelectItem>
                <SelectItem value="failed">Fail</SelectItem>
                <SelectItem value="expired">Pending/Expired</SelectItem>
              </SelectContent>
            </Select>
          </div>
          <div className="space-y-2">
            <Label>From (UTC)</Label>
            <Popover>
              <PopoverTrigger asChild><Button variant="outline" className="w-full justify-start text-left font-normal" disabled={useAllTime}><CalendarIcon className="mr-2 h-4 w-4" />{fromDate ? format(fromDate, "PPP") : "Pick a date"}</Button></PopoverTrigger>
              <PopoverContent className="w-auto p-0"><Calendar mode="single" selected={fromDate} onSelect={(date) => { setFromDate(date || null); setFilters((prev) => ({ ...prev, fromUtc: date ? date.toISOString() : "" })); }} initialFocus /></PopoverContent>
            </Popover>
          </div>
          <div className="space-y-2">
            <Label>To (UTC)</Label>
            <Popover>
              <PopoverTrigger asChild><Button variant="outline" className="w-full justify-start text-left font-normal" disabled={useAllTime}><CalendarIcon className="mr-2 h-4 w-4" />{toDate ? format(toDate, "PPP") : "Pick a date"}</Button></PopoverTrigger>
              <PopoverContent className="w-auto p-0"><Calendar mode="single" selected={toDate} onSelect={(date) => { setToDate(date || null); setFilters((prev) => ({ ...prev, toUtc: date ? date.toISOString() : "" })); }} initialFocus /></PopoverContent>
            </Popover>
          </div>
          <div className="space-y-2 md:col-span-2">
            <Label>Time Range</Label>
            <div className="flex items-center gap-3 rounded-xl border border-slate-200 bg-slate-50 px-3 py-2"><input type="checkbox" checked={useAllTime} onChange={(event) => setUseAllTime(event.target.checked)} /><div className="text-sm text-slate-700">Use all time (ignore date filters)</div></div>
            <p className="text-xs text-slate-500">Uncheck to filter by From/To UTC values.</p>
          </div>
          <div className="space-y-2 md:col-span-3"><Label>Search (session id or customerRef)</Label><Input value={filters.q} onChange={(event) => setFilters((prev) => ({ ...prev, q: event.target.value }))} placeholder="session id / customer reference" /></div>
          <div className="flex items-end gap-2">
            <Button className="bg-orange-500 hover:bg-orange-600" disabled={loading} onClick={() => load()}>Apply</Button>
            <Button variant="outline" disabled={loading} onClick={() => { setUseAllTime(true); setFromDate(null); setToDate(null); load({ q: "", fromUtc: "", toUtc: "", status: "all", tenantId: "", tenantSlug: "" }); }}>Reset</Button>
          </div>
          <div className="md:col-span-4 flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3">
            <div className="text-sm text-slate-600">Use filters above, then export the current page or move through report pages.</div>
            <div className="flex flex-wrap gap-2">
              <Select value={String(filters.take)} onValueChange={(value) => setFilters((prev) => ({ ...prev, take: Number(value), skip: 0 }))}>
                <SelectTrigger className="h-9 w-[120px] bg-white"><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="25">25 / page</SelectItem>
                  <SelectItem value="50">50 / page</SelectItem>
                  <SelectItem value="100">100 / page</SelectItem>
                  <SelectItem value="200">200 / page</SelectItem>
                </SelectContent>
              </Select>
              <Button variant="outline" onClick={exportRows} disabled={!rows.length}>Export CSV</Button>
            </div>
          </div>
        </CardContent>
      </Card>

      <div className="grid gap-3 md:grid-cols-4">
        <Card className="border-slate-200"><CardContent className="pt-4"><div className="text-xs uppercase text-slate-500">Sessions</div><div className="mt-2 text-2xl font-semibold text-slate-900">{summary.total.toLocaleString()}</div><div className="text-xs text-slate-500">Filtered sessions</div></CardContent></Card>
        <Card className="border-slate-200"><CardContent className="pt-4"><div className="text-xs uppercase text-slate-500">Success</div><div className="mt-2 text-2xl font-semibold text-emerald-700">{summary.verified.toLocaleString()}</div><div className="text-xs text-slate-500">Verified KYC</div></CardContent></Card>
        <Card className="border-slate-200 bg-gradient-to-br from-orange-50 via-white to-amber-50 shadow-sm"><CardContent className="pt-4"><div className="text-xs uppercase text-slate-500">Credits Used</div><div className="mt-2 flex items-end gap-2"><div className="text-3xl font-semibold text-slate-950">{summary.creditsUsed.toLocaleString()}</div><Badge className="border border-orange-200 bg-orange-100 text-orange-700 hover:bg-orange-100">Billable</Badge></div><div className="text-xs text-slate-500">Total billable KYC credits across filtered records</div></CardContent></Card>
        <Card className="border-slate-200"><CardContent className="pt-4"><div className="text-xs uppercase text-slate-500">Tenant Credits</div><div className="mt-2 text-2xl font-semibold text-slate-900">{filters.tenantId ? tenantKycCredits.toLocaleString() : "-"}</div><div className="text-xs text-slate-500">{filters.tenantId ? `Used ${tenantKycUsed} this month` : "Select tenant to view"}</div></CardContent></Card>
      </div>
      <Card className="border-slate-200">
        <CardHeader className="border-b border-slate-100 bg-slate-50/70">
          <CardTitle>Sessions</CardTitle>
          <CardDescription>Click view to inspect provider response, documents, and webhook status. Current page shows {rows.length.toLocaleString()} rows.</CardDescription>
        </CardHeader>
        <CardContent className="space-y-4 p-4 md:p-0">
          <div className="space-y-3 md:hidden">
            {rows.map((row, index) => {
              const meta = apiMeta(row.provider);
              const MetaIcon = meta.icon;
              const status = statusBadge(row.status);
              const StatusIcon = status.icon;
              return (
                <div key={row.sessionId} className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
                  <div className="flex items-start justify-between gap-3">
                    <div>
                      <div className="text-xs uppercase tracking-[0.18em] text-slate-500">#{filters.skip + index + 1}</div>
                      <div className="mt-1 text-sm font-semibold text-slate-900">{row.userEmail || "public"}</div>
                      <div className="text-xs text-slate-500">{row.tenantName || row.tenantSlug}</div>
                    </div>
                    <Badge className={status.className}><StatusIcon className="mr-1.5 h-3.5 w-3.5" />{status.label}</Badge>
                  </div>
                  <div className="mt-4 grid grid-cols-2 gap-3 text-sm">
                    <div className="col-span-2">
                      <div className="text-xs uppercase text-slate-500">API Name</div>
                      <div className="mt-1"><Badge className={meta.className}><MetaIcon className="mr-1.5 h-3.5 w-3.5" />{meta.label}</Badge></div>
                    </div>
                    <div>
                      <div className="text-xs uppercase text-slate-500">Customer Ref</div>
                      <div className="mt-1 font-medium text-slate-900 break-all">{row.customerRef || "-"}</div>
                    </div>
                    <div>
                      <div className="text-xs uppercase text-slate-500">Credits</div>
                      <div className="mt-1 font-medium text-slate-900">{Number(row.creditsUsed || 0)}</div>
                    </div>
                    <div className="col-span-2">
                      <div className="text-xs uppercase text-slate-500">Session ID</div>
                      <div className="mt-1 font-medium text-slate-900 break-all">{row.sessionId}</div>
                    </div>
                    <div>
                      <div className="text-xs uppercase text-slate-500">Doc Type</div>
                      <div className="mt-1 font-medium text-slate-900">{Array.isArray(row.docTypes) ? row.docTypes.join(", ") : "-"}</div>
                    </div>
                    <div>
                      <div className="text-xs uppercase text-slate-500">Webhook</div>
                      <div className="mt-1 font-medium text-slate-900">{row.webhook ? `${row.webhook.statusCode} (${row.webhook.ok ? "ok" : "failed"})` : "-"}</div>
                    </div>
                    <div className="col-span-2">
                      <div className="text-xs uppercase text-slate-500">Created</div>
                      <div className="mt-1 font-medium text-slate-900">{formatDate(row.createdAtUtc)}</div>
                    </div>
                  </div>
                  <div className="mt-4 flex justify-end">
                    <Button variant="outline" size="sm" onClick={async () => {
                      setActive(row);
                      setActiveDetail(null);
                      setActiveFileIndex(0);
                      setOpen(true);
                      try {
                        const detail = await getPlatformKycReportSession(row.sessionId, true);
                        setActiveDetail(detail);
                      } catch (error) {
                        toast.error(error?.message || "Failed to load session detail");
                      }
                    }}>View</Button>
                  </div>
                </div>
              );
            })}
            {rows.length === 0 ? <div className="rounded-2xl border border-dashed border-slate-200 px-4 py-10 text-center text-slate-500">No KYC sessions found.</div> : null}
          </div>
          <div className="hidden overflow-x-auto md:block">
            <table className="w-full min-w-[1200px] text-sm">
              <thead className="sticky top-0 z-10 bg-slate-50 shadow-sm">
                <tr>
                  <th className="px-4 py-3 text-left font-semibold text-slate-600">S.No</th>
                  <th className="px-4 py-3 text-left font-semibold text-slate-600">Date</th>
                  <th className="px-4 py-3 text-left font-semibold text-slate-600">User</th>
                  <th className="px-4 py-3 text-left font-semibold text-slate-600">API Name</th>
                  <th className="px-4 py-3 text-left font-semibold text-slate-600">Session Id</th>
                  <th className="px-4 py-3 text-left font-semibold text-slate-600">Customer Ref</th>
                  <th className="px-4 py-3 text-left font-semibold text-slate-600">Doc Type</th>
                  <th className="px-4 py-3 text-left font-semibold text-slate-600">Status</th>
                  <th className="px-4 py-3 text-left font-semibold text-slate-600">Credits</th>
                  <th className="px-4 py-3 text-left font-semibold text-slate-600">Webhook</th>
                  <th className="px-4 py-3 text-left font-semibold text-slate-600">View</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row, index) => {
                  const meta = apiMeta(row.provider);
                  const MetaIcon = meta.icon;
                  const status = statusBadge(row.status);
                  const StatusIcon = status.icon;
                  return (
                    <tr key={row.sessionId} className="border-t border-slate-100">
                      <td className="px-4 py-3 text-slate-600">{index + 1}</td>
                      <td className="px-4 py-3 text-slate-600">{formatDate(row.createdAtUtc)}</td>
                      <td className="px-4 py-3"><div className="text-slate-900">{row.userEmail || "public"}</div><div className="text-xs text-slate-500">{row.tenantName || row.tenantSlug}</div></td>
                      <td className="px-4 py-3 text-slate-600"><Badge className={meta.className}><MetaIcon className="mr-1.5 h-3.5 w-3.5" />{meta.label}</Badge></td>
                      <td className="px-4 py-3 text-slate-600">{row.sessionId}</td>
                      <td className="px-4 py-3 text-slate-600">{row.customerRef || "-"}</td>
                      <td className="px-4 py-3 text-slate-600">{Array.isArray(row.docTypes) ? row.docTypes.join(", ") : "-"}</td>
                      <td className="px-4 py-3"><Badge className={status.className}><StatusIcon className="mr-1.5 h-3.5 w-3.5" />{status.label}</Badge></td>
                      <td className="px-4 py-3 text-slate-600">{Number(row.creditsUsed || 0)}</td>
                      <td className="px-4 py-3 text-slate-600">{row.webhook ? `${row.webhook.statusCode} (${row.webhook.ok ? "ok" : "failed"})` : "-"}</td>
                      <td className="px-4 py-3"><Button variant="outline" size="sm" onClick={async () => {
                        setActive(row);
                        setActiveDetail(null);
                        setActiveFileIndex(0);
                        setOpen(true);
                        try {
                          const detail = await getPlatformKycReportSession(row.sessionId, true);
                          setActiveDetail(detail);
                        } catch (error) {
                          toast.error(error?.message || "Failed to load session detail");
                        }
                      }}>View</Button></td>
                    </tr>
                  );
                })}
                {rows.length === 0 ? <tr><td colSpan={11} className="px-4 py-10 text-center text-slate-500">No KYC sessions found.</td></tr> : null}
              </tbody>
            </table>
          </div>
        </CardContent>
        <div className="flex flex-col gap-3 border-t border-slate-100 bg-slate-50 px-6 py-4 md:flex-row md:items-center md:justify-between">
          <div className="text-sm text-slate-600">
            Showing {rows.length ? filters.skip + 1 : 0}–{Math.min(filters.skip + rows.length, totalCount || rows.length)} of {(totalCount || rows.length).toLocaleString()} records
          </div>
          <div className="flex items-center gap-2">
            <Button variant="outline" className="rounded-xl" disabled={loading || filters.skip <= 0} onClick={() => load({ skip: Math.max(0, filters.skip - filters.take) })}>Previous</Button>
            <div className="min-w-[110px] text-center text-sm text-slate-600">Page {Math.floor(filters.skip / filters.take) + 1}</div>
            <Button variant="outline" className="rounded-xl" disabled={loading || filters.skip + rows.length >= (totalCount || 0)} onClick={() => load({ skip: filters.skip + filters.take })}>Next</Button>
          </div>
        </div>
      </Card>

      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent className="max-w-6xl">
          <DialogHeader><DialogTitle>KYC Record</DialogTitle></DialogHeader>
          {!active ? null : (
            <>
              <div className="mb-4 grid gap-3 md:grid-cols-4">
                <Card className="border-slate-200"><CardContent className="pt-4"><div className="text-xs uppercase text-slate-500">Customer Ref</div><div className="mt-2 text-sm font-semibold text-slate-900">{activeDetail?.customerRef || active.customerRef || "-"}</div></CardContent></Card>
                <Card className="border-slate-200"><CardContent className="pt-4"><div className="text-xs uppercase text-slate-500">Credits Used</div><div className="mt-2 text-sm font-semibold text-slate-900">{Number(activeDetail?.creditsUsed || active?.creditsUsed || 0)}</div></CardContent></Card>
                <Card className="border-slate-200"><CardContent className="pt-4"><div className="text-xs uppercase text-slate-500">Created</div><div className="mt-2 text-sm font-semibold text-slate-900">{formatDate(activeDetail?.createdAtUtc || active.createdAtUtc)}</div></CardContent></Card>
                <Card className="border-slate-200"><CardContent className="pt-4"><div className="text-xs uppercase text-slate-500">Status</div><div className="mt-2 text-sm font-semibold text-slate-900">{(() => { const status = statusBadge(activeDetail?.status || active.status || ""); const StatusIcon = status.icon; return <Badge className={status.className}><StatusIcon className="mr-1.5 h-3.5 w-3.5" />{status.label}</Badge>; })()}</div></CardContent></Card>
              </div>

              {prettifyFailureReason(activeDetail?.failureReason || active?.failureReason) ? (
                <div className="mb-4 rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-800">
                  <div className="text-xs font-semibold uppercase tracking-[0.18em] text-rose-600">Failure Reason</div>
                  <div className="mt-1">{prettifyFailureReason(activeDetail?.failureReason || active?.failureReason)}</div>
                </div>
              ) : null}

              <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
                <div className="rounded-2xl border border-slate-200 bg-slate-50 p-3">
                  <div className="mb-2 flex items-center justify-between text-xs font-medium text-slate-500">
                    <span>{isGstProvider ? "Document preview" : "Document preview"}</span>
                    {files.length ? <Badge variant="outline">{files.length} files</Badge> : null}
                  </div>
                  {previewUrl ? (
                    <div className="relative h-[540px] w-full overflow-hidden rounded-xl bg-white">
                      <iframe title="doc-preview" src={previewUrl} className="h-full w-full" />
                      {files.length > 1 ? (
                        <div className="absolute left-3 right-3 top-3 flex items-center justify-between rounded-xl border border-slate-200 bg-white/90 px-3 py-2 text-xs text-slate-700 backdrop-blur">
                          <div className="font-medium">{`${String(activeFile?.doctype || "").toUpperCase() || "FILE"} (${activeFileIndex + 1}/${files.length})`}</div>
                          <div className="flex items-center gap-2">
                            <Button variant="outline" className="h-7 rounded-lg px-2" disabled={activeFileIndex <= 0} onClick={() => setActiveFileIndex((index) => Math.max(0, index - 1))}>Prev</Button>
                            <Button variant="outline" className="h-7 rounded-lg px-2" disabled={activeFileIndex >= files.length - 1} onClick={() => setActiveFileIndex((index) => Math.min(files.length - 1, index + 1))}>Next</Button>
                          </div>
                        </div>
                      ) : null}
                    </div>
                  ) : !isGstProvider ? (
                    <KycPreviewCard collected={activeDetail?.result?.collected || {}} active={activeDetail || active} />
                  ) : (
                    <div className="flex h-[540px] items-center justify-center rounded-xl border border-dashed border-slate-200 bg-white text-sm text-slate-500">No preview file returned in this AppyFlow session.</div>
                  )}

                  {files.length > 0 ? (
                    <div className="mt-3 grid gap-2">
                      {files.map((file, index) => (
                        <button key={`${file.uri}-${index}`} type="button" className={`w-full rounded-xl border px-3 py-2 text-left text-xs transition ${index === activeFileIndex ? "border-orange-300 bg-orange-50 shadow-sm" : "border-slate-200 bg-white hover:bg-slate-50"}`} onClick={() => setActiveFileIndex(index)}>
                          <div className="flex items-start gap-3">
                            <div className={`mt-0.5 rounded-lg p-2 ${String(file?.mime || "").startsWith("image/") ? "bg-violet-100 text-violet-700" : "bg-sky-100 text-sky-700"}`}>{String(file?.mime || "").startsWith("image/") ? <ImageIcon className="h-4 w-4" /> : <FileText className="h-4 w-4" />}</div>
                            <div className="min-w-0 flex-1">
                              <div className="font-medium text-slate-900">{file.name || file.uri}</div>
                              <div className="mt-1 flex flex-wrap gap-2 text-slate-500"><span>{file.doctype || "Document"}</span><span>•</span><span>{file.mime || "file"}</span><span>•</span><span>{(Number(file.sizeBytes || 0) / 1024).toFixed(1)} KB</span></div>
                            </div>
                          </div>
                        </button>
                      ))}
                    </div>
                  ) : null}
                </div>

                <div className="rounded-2xl border border-slate-200 bg-white p-3">
                  <div className="mb-2 flex items-center justify-between text-xs font-medium text-slate-500">
                    <span>Extracted fields</span>
                    <div className="flex items-center gap-2">
                      <Badge className={activeApi.className}>{(() => { const ApiIcon = activeApi.icon; return <><ApiIcon className="mr-1.5 h-3.5 w-3.5" />{activeApi.label}</>; })()}</Badge>
                    </div>
                  </div>

                  {(() => {
                    const result = activeDetail?.result || active?.result || {};
                    const collected = result?.collected || {};
                    if (isGstProvider) {
                      const taxpayer = result?.taxpayerInfo || {};
                      return (
                        <div className="grid grid-cols-2 gap-3 text-sm">
                          <div className="col-span-2">
                            <div className="text-slate-500">Trade Name</div>
                            <div className="font-medium text-slate-900">{safeGet(taxpayer, "tradeNam", "-")}</div>
                          </div>
                          <div>
                            <div className="text-slate-500">GST No</div>
                            <div className="font-medium text-slate-900">{safeGet(result, "gstNo", safeGet(taxpayer, "gstin", "-"))}</div>
                          </div>
                          <div>
                            <div className="text-slate-500">PAN</div>
                            <div className="font-medium text-slate-900">{safeGet(taxpayer, "panNo", "-")}</div>
                          </div>
                          <div>
                            <div className="text-slate-500">Taxpayer Type</div>
                            <div className="font-medium text-slate-900">{safeGet(taxpayer, "ctb", "-")}</div>
                          </div>
                          <div>
                            <div className="text-slate-500">Status</div>
                            <div className="font-medium text-slate-900">{safeGet(taxpayer, "sts", "-")}</div>
                          </div>
                          <div className="col-span-2">
                            <div className="text-slate-500">Principal Address</div>
                            <div className="font-medium text-slate-900">
                              {[
                                safeGet(taxpayer?.pradr?.addr, "bno", ""),
                                safeGet(taxpayer?.pradr?.addr, "st", ""),
                                safeGet(taxpayer?.pradr?.addr, "loc", ""),
                                safeGet(taxpayer?.pradr?.addr, "dst", ""),
                                safeGet(taxpayer?.pradr?.addr, "stcd", ""),
                                safeGet(taxpayer?.pradr?.addr, "pncd", ""),
                              ].filter(Boolean).join(", ") || "-"}
                            </div>
                          </div>
                        </div>
                      );
                    }

                    const digilockerId = safeGet(result?.userDetails, "digilockerid", "-");
                    const aadhaarNo = safeGet(collected, "aadhaarNumber", "-");
                    const photo = toDataUrl(collected.photoBase64);
                    return (
                      <div className="space-y-3">
                        {photo ? (
                          <div className="flex items-start gap-3">
                            <img src={photo} alt="photo" className="h-24 w-24 rounded-2xl border border-slate-200 object-cover" />
                            <div className="text-sm">
                              <div className="text-slate-500">Photo</div>
                              <div className="text-slate-900">Extracted from Aadhaar XML</div>
                            </div>
                          </div>
                        ) : null}

                        <div className="grid grid-cols-2 gap-3 text-sm">
                          <div className="col-span-2">
                            <div className="text-slate-500">DigiLocker ID</div>
                            <div className="font-medium text-slate-900">{digilockerId}</div>
                          </div>
                          <div>
                            <div className="text-slate-500">Mobile</div>
                            <div className="font-medium text-slate-900">{safeGet(collected, "mobile", "-")}</div>
                          </div>
                          <div>
                            <div className="text-slate-500">Email</div>
                            <div className="truncate font-medium text-slate-900">{safeGet(collected, "email", "-")}</div>
                          </div>
                          <div>
                            <div className="text-slate-500">Gender</div>
                            <div className="font-medium text-slate-900">{safeGet(collected, "gender", "-")}</div>
                          </div>
                          <div>
                            <div className="text-slate-500">Age</div>
                            <div className="font-medium text-slate-900">{String(safeGet(collected, "ageYears", "-"))}</div>
                          </div>
                          <div>
                            <div className="text-slate-500">PAN</div>
                            <div className="font-medium text-slate-900">{safeGet(collected, "pan", "-")}</div>
                          </div>
                          <div>
                            <div className="text-slate-500">DL No</div>
                            <div className="font-medium text-slate-900">{safeGet(collected, "drivingLicense", "-")}</div>
                          </div>
                          <div className="col-span-2">
                            <div className="text-slate-500">Aadhaar No</div>
                            <div className="font-medium text-slate-900">{aadhaarNo}</div>
                          </div>
                        </div>
                      </div>
                    );
                  })()}

                  <div className="mt-4 rounded-2xl border border-slate-200 bg-slate-50 p-3">
                    <div className="mb-2 flex items-center justify-between text-xs font-medium text-slate-500">
                      <span>{publicTabLabel}</span>
                      <Button variant="outline" size="sm" className="h-8 rounded-lg" onClick={() => copyJson(activeDetail?.result || active.result || {}, "Response")}>
                        <Copy className="mr-1.5 h-3.5 w-3.5" />Copy JSON
                      </Button>
                    </div>
                    <pre className="max-h-[240px] overflow-auto rounded-xl bg-white p-3 text-xs text-slate-700">{formatResponse(activeDetail?.result || active.result || {})}</pre>
                  </div>

                  <div className="mt-4 rounded-2xl border border-slate-200 bg-slate-50 p-3">
                    <div className="mb-2 flex items-center justify-between text-xs font-medium text-slate-500">
                      <span>{rawTabLabel}</span>
                      <Button variant="outline" size="sm" className="h-8 rounded-lg" onClick={() => copyJson(activeDetail?.rawResultJson || active.rawResultJson || "{}", "Raw response")}>
                        <Copy className="mr-1.5 h-3.5 w-3.5" />Copy JSON
                      </Button>
                    </div>
                    <pre className="max-h-[220px] overflow-auto rounded-xl bg-white p-3 text-xs text-slate-700">{formatResponse(activeDetail?.rawResultJson || active.rawResultJson || "{}")}</pre>
                  </div>
                </div>
              </div>
            </>
          )}
        </DialogContent>
      </Dialog>
    </div>
  );
}
