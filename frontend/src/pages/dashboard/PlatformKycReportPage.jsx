import { useEffect, useMemo, useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
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
  const [expandedResponseTabs, setExpandedResponseTabs] = useState({ public: false, raw: false });
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
    total: rows.length,
    verified: rows.filter((row) => String(row?.status || "").toLowerCase() === "verified").length,
    creditsUsed: rows.reduce((sum, row) => sum + Number(row?.creditsUsed || 0), 0),
  }), [rows]);

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
          <CardDescription>Click view to inspect provider response, documents, and webhook status.</CardDescription>
        </CardHeader>
        <CardContent className="p-0">
          <div className="overflow-x-auto">
            <table className="w-full min-w-[1200px] text-sm">
              <thead className="bg-slate-50">
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
                        setExpandedResponseTabs({ public: false, raw: false });
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
      </Card>

      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent className="max-w-5xl">
          <DialogHeader><DialogTitle>KYC Session Detail</DialogTitle></DialogHeader>
          {!active ? null : (
            <div className="grid gap-4 md:grid-cols-[1.2fr_1fr]">
              <div className="space-y-4">
                <div className={`rounded-2xl border border-slate-200 bg-gradient-to-br ${activeApi.accent} p-4 text-sm`}>
                  <div className="grid gap-2 md:grid-cols-2">
                    <div><span className="text-slate-500">Session</span><div className="font-medium">{activeDetail?.sessionId || active.sessionId}</div></div>
                    <div><span className="text-slate-500">Customer Ref</span><div className="font-medium">{activeDetail?.customerRef || active.customerRef || "-"}</div></div>
                    <div><span className="text-slate-500">User</span><div className="font-medium">{activeDetail?.userEmail || active.userEmail || "public"}</div></div>
                    <div><span className="text-slate-500">Tenant</span><div className="font-medium">{activeDetail?.tenantName || active.tenantName || active.tenantSlug}</div></div>
                    <div><span className="text-slate-500">API Name</span><div className="mt-1">{(() => { const ApiIcon = activeApi.icon; return <Badge className={activeApi.className}><ApiIcon className="mr-1.5 h-3.5 w-3.5" />{activeApi.label}</Badge>; })()}</div></div>
                    <div><span className="text-slate-500">Status</span><div className="mt-1">{(() => { const status = statusBadge(activeDetail?.status || active.status); const StatusIcon = status.icon; return <Badge className={status.className}><StatusIcon className="mr-1.5 h-3.5 w-3.5" />{status.label}</Badge>; })()}</div></div>
                    <div><span className="text-slate-500">Doc Types</span><div className="font-medium">{Array.isArray(activeDetail?.docTypes || active.docTypes) ? (activeDetail?.docTypes || active.docTypes).join(", ") : "-"}</div></div>
                    <div><span className="text-slate-500">Credits Used</span><div className="font-medium">{Number(activeDetail?.creditsUsed || active?.creditsUsed || 0)}</div></div>
                  </div>
                </div>

                <Tabs defaultValue="public">
                  <TabsList><TabsTrigger value="public">{publicTabLabel}</TabsTrigger><TabsTrigger value="raw">{rawTabLabel}</TabsTrigger></TabsList>
                  <TabsContent value="public">
                    <div className="space-y-2">
                      <div className="flex items-center justify-between">
                        <div className="text-xs font-medium text-slate-500">Formatted partner response</div>
                        <div className="flex gap-2">
                          <Button variant="outline" size="sm" className="h-8 rounded-lg" onClick={() => copyJson(activeDetail?.result || active.result || {}, "Formatted response")}><Copy className="mr-1.5 h-3.5 w-3.5" />Copy JSON</Button>
                          <Button variant="outline" size="sm" className="h-8 rounded-lg" onClick={() => setExpandedResponseTabs((prev) => ({ ...prev, public: !prev.public }))}>{expandedResponseTabs.public ? "Collapse" : "Expand"}</Button>
                        </div>
                      </div>
                      <pre className={`${expandedResponseTabs.public ? "max-h-[560px]" : "max-h-[320px]"} overflow-auto rounded-xl border border-slate-200 bg-slate-50 p-3 text-xs`}>{formatResponse(activeDetail?.result || active.result || {})}</pre>
                    </div>
                  </TabsContent>
                  <TabsContent value="raw">
                    <div className="space-y-2">
                      <div className="flex items-center justify-between">
                        <div className="text-xs font-medium text-slate-500">Exact response received from provider</div>
                        <div className="flex gap-2">
                          <Button variant="outline" size="sm" className="h-8 rounded-lg" onClick={() => copyJson(activeDetail?.rawResultJson || active.rawResultJson || "{}", "Raw response")}><Copy className="mr-1.5 h-3.5 w-3.5" />Copy JSON</Button>
                          <Button variant="outline" size="sm" className="h-8 rounded-lg" onClick={() => setExpandedResponseTabs((prev) => ({ ...prev, raw: !prev.raw }))}>{expandedResponseTabs.raw ? "Collapse" : "Expand"}</Button>
                        </div>
                      </div>
                      <pre className={`${expandedResponseTabs.raw ? "max-h-[560px]" : "max-h-[320px]"} overflow-auto rounded-xl border border-slate-200 bg-slate-50 p-3 text-xs`}>{formatResponse(activeDetail?.rawResultJson || active.rawResultJson || "{}")}</pre>
                    </div>
                  </TabsContent>
                </Tabs>
              </div>
              <div className="space-y-4">
                <div className="rounded-xl border border-slate-200 p-4 text-sm">
                  <div className="flex items-center justify-between">
                    <div className="font-medium text-slate-900">{isGstProvider ? "AppyFlow Attachments" : "DigiLocker Documents"}</div>
                    <Badge variant="outline">{files.length} files</Badge>
                  </div>
                  {files.length === 0 ? (
                    <div className="mt-3 text-slate-500">{isGstProvider ? "No AppyFlow attachments returned for this session." : "No documents in this session."}</div>
                  ) : (
                    <div className="mt-3 space-y-2">
                      {files.map((file, index) => (
                        <button key={`${file.uri}-${index}`} type="button" className={`w-full rounded-xl border px-3 py-2 text-left text-xs transition ${index === activeFileIndex ? "border-orange-300 bg-orange-50 shadow-sm" : "border-slate-200 hover:bg-slate-50"}`} onClick={() => setActiveFileIndex(index)}>
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
                  )}
                </div>
                {previewUrl ? <iframe title="doc-preview" src={previewUrl} className="h-[420px] w-full rounded-xl border border-slate-200" /> : !isGstProvider ? <KycPreviewCard collected={activeDetail?.result?.collected || {}} active={activeDetail || active} /> : <div className="flex h-[420px] items-center justify-center rounded-xl border border-dashed border-slate-200 text-sm text-slate-500">Select a document to preview.</div>}
              </div>
            </div>
          )}
        </DialogContent>
      </Dialog>
    </div>
  );
}
