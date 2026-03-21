import { useEffect, useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import { listKycSessions, getKycSession } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Badge } from "@/components/ui/badge";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { toast } from "sonner";

function safeGet(obj, key, fallback = "") {
  try {
    if (!obj) return fallback;
    const v = obj[key];
    if (v === null || v === undefined) return fallback;
    return v;
  } catch {
    return fallback;
  }
}

function normalizeStatus(status) {
  const s = String(status || "").toLowerCase();
  if (s === "verified" || s === "success") return { label: "Success", className: "border border-emerald-200 bg-emerald-100 text-emerald-700 hover:bg-emerald-100" };
  if (s === "failed" || s === "error") return { label: "Fail", className: "border border-rose-200 bg-rose-100 text-rose-700 hover:bg-rose-100" };
  if (s === "created" || s === "redirected" || s === "pending" || s === "expired") return { label: "Pending", className: "border border-amber-200 bg-amber-100 text-amber-700 hover:bg-amber-100" };
  if (s) return { label: s, className: "border border-slate-200 bg-slate-100 text-slate-700 hover:bg-slate-100" };
  return { label: "-", className: "border border-slate-200 bg-slate-100 text-slate-700 hover:bg-slate-100" };
}

function isAadhaarProvider(record) {
  return String(record?.provider || "").toLowerCase() === "aadhaarxml";
}

function effectiveStatus(record) {
  if (!record) return "";
  if (isAadhaarProvider(record)) {
    const candidate = String(record?.resultStatus || record?.result?.status || "").trim();
    if (candidate) return candidate;
  }
  return String(record?.status || "").trim();
}

function effectiveFailureReason(record) {
  if (!record) return "";
  if (isAadhaarProvider(record)) {
    const candidate = String(record?.resultFailureReason || record?.result?.failureReason || "").trim();
    if (candidate) return candidate;
  }
  return String(record?.failureReason || "").trim();
}

function toDataUrl(base64) {
  const b = String(base64 || "").trim();
  if (!b) return "";
  return `data:image/jpeg;base64,${b}`;
}

function decodeBase64ToBlobUrl(base64, mime = "application/pdf") {
  const b = String(base64 || "").trim();
  if (!b) return "";
  const binary = atob(b);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  const blob = new Blob([bytes], { type: mime || "application/octet-stream" });
  return URL.createObjectURL(blob);
}

function formatDate(value) {
  if (!value) return "-";
  try {
    return new Date(value).toLocaleString();
  } catch {
    return String(value);
  }
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

function downloadBlobUrl(url, filename = "kyc-document.pdf") {
  if (!url) return;
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = filename;
  anchor.click();
}

function renderAttributeGroups(rawAttributes) {
  const groups = rawAttributes && typeof rawAttributes === "object" ? Object.entries(rawAttributes) : [];
  if (!groups.length) return null;
  return (
    <div className="mt-4 rounded-2xl border border-slate-200 bg-slate-50 p-3">
      <div className="mb-2 text-xs font-medium text-slate-500">Raw XML Attributes</div>
      <div className="grid gap-3 md:grid-cols-3">
        {groups.map(([groupName, values]) => {
          const entries = values && typeof values === "object" ? Object.entries(values) : [];
          return (
            <div key={groupName} className="rounded-xl border border-slate-200 bg-white p-3">
              <div className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-500">{groupName}</div>
              {entries.length ? (
                <div className="space-y-1 text-xs">
                  {entries.map(([key, value]) => (
                    <div key={key} className="break-all">
                      <span className="text-slate-500">{key}:</span>{" "}
                      <span className="font-medium text-slate-900">{String(value ?? "")}</span>
                    </div>
                  ))}
                </div>
              ) : (
                <div className="text-xs text-slate-400">No values</div>
              )}
            </div>
          );
        })}
      </div>
    </div>
  );
}

function isWithinDateRange(value, fromDate, toDate) {
  if (!value) return true;
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return true;
  if (fromDate && date < new Date(`${fromDate}T00:00:00`)) return false;
  if (toDate && date > new Date(`${toDate}T23:59:59.999`)) return false;
  return true;
}

function prettifyFailureReason(reason) {
  const text = String(reason || "").trim();
  if (!text) return "";
  const normalized = text.toLowerCase();
  if (normalized.includes("invalid_grant_type") || normalized.includes("disable openid")) {
    return "DigiLocker token exchange failed because the client is still using openid in its allowed scopes. Disable openid in the DigiLocker client configuration and try again.";
  }
  if (normalized.startsWith("provider_error:")) {
    return text.slice("provider_error:".length).trim() || "Provider returned an error.";
  }
  return text;
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

function KycPreviewCard({ collected, active }) {
  const name = safeGet(collected, "name", "-");
  const dob = safeGet(collected, "dob", "-");
  const gender = safeGet(collected, "gender", "-");
  const fatherName = safeGet(collected, "fatherName", "") || safeGet(collected, "careOfRaw", "-");
  const aadhaar = safeGet(collected, "aadhaarNumberFull", "") || safeGet(collected, "aadhaarNumber", "") || "-";
  const referenceId = safeGet(collected, "referenceId", "-");
  const pan = safeGet(collected, "pan", "-");
  const address = safeGet(collected, "address", "-");
  const photo = toDataUrl(collected.photoBase64);
  const email = safeGet(collected, "email", "-");
  const mobile = safeGet(collected, "mobileFromXml", "-");
  const enteredMobile = safeGet(collected, "mobileNumber", "-");
  const docType = (Array.isArray(active?.docTypes) && active.docTypes[0]) ? String(active.docTypes[0]).toUpperCase() : "KYC";
  const signatureValid = String(safeGet(collected, "signatureValid", "-"));
  const verified = String(safeGet(collected, "verificationStatus", effectiveStatus(active))).toLowerCase() === "verified";
  const failureReason = safeGet(collected, "failureReason", "") || effectiveFailureReason(active);
  const showSignature = isAadhaarProvider(active);

  return (
    <div className="relative h-[540px] w-full overflow-hidden rounded-xl bg-white">
      <div className="relative p-5">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-3">
            <div className="flex h-10 w-10 items-center justify-center rounded-xl border border-slate-200 bg-slate-50 text-sm font-bold text-slate-900">
              ID
            </div>
            <div>
              <div className="text-sm font-semibold text-slate-900">Aadhaar XML Verification</div>
              <div className="text-xs text-slate-500">{docType} record preview</div>
            </div>
          </div>
          <div className="flex gap-2">
            <Badge className={verified ? "bg-emerald-600 hover:bg-emerald-600" : "bg-rose-600 hover:bg-rose-600"}>
              {verified ? "Verified" : "Failed"}
            </Badge>
            {showSignature ? (
              <Badge variant="outline" className="border-slate-300 bg-white">
                {signatureValid === "True" || signatureValid === "true" ? "Digital Signature Valid" : "Signature Check Failed"}
              </Badge>
            ) : null}
          </div>
        </div>

        {failureReason ? (
          <div className="mt-4 rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-xs text-rose-900">
            <div className="font-semibold uppercase tracking-wide text-rose-600">Failure reason</div>
            <div className="mt-1">{failureReason}</div>
          </div>
        ) : null}

        <div className="mt-4 grid grid-cols-[1fr_140px] gap-4 rounded-2xl border border-slate-200 p-4">
          <div className="space-y-2 text-xs">
            <div className="grid grid-cols-2 gap-3">
              <div>
                <div className="text-slate-500">Name</div>
                <div className="font-semibold text-slate-900">{name}</div>
              </div>
              <div>
                <div className="text-slate-500">DOB</div>
                <div className="font-semibold text-slate-900">{dob}</div>
              </div>
              <div>
                <div className="text-slate-500">Gender</div>
                <div className="font-semibold text-slate-900">{gender}</div>
              </div>
              <div>
                <div className="text-slate-500">Father / Guardian</div>
                <div className="font-semibold text-slate-900">{fatherName}</div>
              </div>
              <div>
                <div className="text-slate-500">Aadhaar No</div>
                <div className="font-semibold text-slate-900">{aadhaar}</div>
              </div>
              <div>
                <div className="text-slate-500">Reference ID</div>
                <div className="font-semibold text-slate-900">{referenceId}</div>
              </div>
              <div>
                <div className="text-slate-500">XML Mobile Hash</div>
                <div className="font-semibold text-slate-900">{mobile}</div>
              </div>
              <div>
                <div className="text-slate-500">Entered Mobile</div>
                <div className="font-semibold text-slate-900">{enteredMobile}</div>
              </div>
              <div>
                <div className="text-slate-500">Email</div>
                <div className="font-semibold text-slate-900">{email}</div>
              </div>
              <div>
                <div className="text-slate-500">PAN</div>
                <div className="font-semibold text-slate-900">{pan}</div>
              </div>
              <div className="col-span-2">
                <div className="text-slate-500">Address</div>
                <div className="font-semibold text-slate-900">{address}</div>
              </div>
              {showSignature ? (
                <div className="col-span-2">
                  <div className="text-slate-500">Digital Signature</div>
                  <div className="font-semibold text-slate-900">
                    {signatureValid === "True" || signatureValid === "true" ? "Valid UIDAI-signed XML" : "Signature validation failed"}
                  </div>
                </div>
              ) : null}
            </div>
          </div>

          <div className="flex flex-col items-center gap-2">
            {photo ? (
              <img src={photo} alt="photo" className="h-[150px] w-[120px] rounded-xl border border-slate-200 object-cover" />
            ) : (
              <div className="flex h-[150px] w-[120px] items-center justify-center rounded-xl border border-slate-200 bg-slate-50 text-xs text-slate-400">
                No photo
              </div>
            )}
            <div className="text-[11px] text-slate-500">Offline XML photo</div>
          </div>
        </div>

        <div className="mt-3 text-[11px] text-slate-500">
          Preview shows stored Aadhaar XML values, failure state, and digital signature status.
        </div>
      </div>
    </div>
  );
}

export default function KycReportsPage() {
  const navigate = useNavigate();
  const [rows, setRows] = useState([]);
  const [busy, setBusy] = useState(false);
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(null);
  const [activeDetail, setActiveDetail] = useState(null);
  const [previewUrl, setPreviewUrl] = useState("");
  const [activeFileIndex, setActiveFileIndex] = useState(0);
  const [filters, setFilters] = useState({ q: "", status: "all", api: "all", fromDate: "", toDate: "", pageSize: 10, page: 1 });

  function mapRequestedToDoctype(req) {
    const r = String(req || "").trim().toUpperCase();
    if (!r) return "";
    if (r === "PAN") return "PANCR";
    if (r === "DL" || r === "DRIVING_LICENCE" || r === "DRIVINGLICENSE" || r === "DRIVING-LICENCE") return "DRVLC";
    if (r === "AADHAAR" || r === "AADHAR") return "AADHAAR_REPORT";
    return r;
  }

  function pickBestFileIndex(files, record) {
    if (!Array.isArray(files) || files.length === 0) return 0;
    const requested = (Array.isArray(record?.docTypes) && record.docTypes[0]) ? String(record.docTypes[0]) : "";
    const want = mapRequestedToDoctype(requested);
    if (want) {
      const idx = files.findIndex((f) => String(f?.doctype || "").toUpperCase() === want);
      if (idx >= 0) return idx;
      // Aadhaar can ship as XML too; fall back to ADHAR if no report is present.
      if (want === "AADHAAR_REPORT") {
        const idx2 = files.findIndex((f) => String(f?.doctype || "").toUpperCase() === "ADHAR");
        if (idx2 >= 0) return idx2;
      }
    }
    return 0;
  }

  async function refresh() {
    setBusy(true);
    try {
      const res = await listKycSessions({ take: 100, includeParsed: true });
      setRows(Array.isArray(res) ? res : []);
    } catch (e) {
      toast.error(e?.message || "Failed to load KYC reports");
    } finally {
      setBusy(false);
    }
  }

  useEffect(() => {
    refresh();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    return () => {
      if (previewUrl) URL.revokeObjectURL(previewUrl);
    };
  }, [previewUrl]);

  const sorted = useMemo(() => {
    return [...rows].sort((a, b) => String(b?.createdAtUtc || "").localeCompare(String(a?.createdAtUtc || "")));
  }, [rows]);

  const filtered = useMemo(() => {
    const query = filters.q.trim().toLowerCase();
    return sorted.filter((row) => {
      if (!isWithinDateRange(row?.createdAtUtc, filters.fromDate, filters.toDate)) return false;
      const status = String(effectiveStatus(row) || "").toLowerCase();
      const provider = String(row?.provider || "").toLowerCase();
      const matchesStatus = filters.status === "all" || status === filters.status;
      const matchesApi = filters.api === "all" || provider === filters.api;
      const haystack = [
        row?.sessionId,
        row?.customerRef,
        row?.provider,
        row?.status,
        row?.docTypes?.join(" "),
                        row?.collected?.mobileFromXml,
                        row?.collected?.mobileNumber,
                        row?.collected?.mobile,
                        row?.collected?.email,
                        row?.collected?.name,
      ].filter(Boolean).join(" ").toLowerCase();
      const matchesQuery = !query || haystack.includes(query);
      return matchesStatus && matchesApi && matchesQuery;
    });
  }, [filters, sorted]);

  const totalPages = useMemo(() => Math.max(1, Math.ceil(filtered.length / filters.pageSize)), [filtered.length, filters.pageSize]);

  const pagedRows = useMemo(() => {
    const start = (filters.page - 1) * filters.pageSize;
    return filtered.slice(start, start + filters.pageSize);
  }, [filtered, filters.page, filters.pageSize]);

  const summary = useMemo(() => {
    const total = filtered.length;
    const verified = filtered.filter((row) => String(effectiveStatus(row) || "").toLowerCase() === "verified").length;
    const failed = filtered.filter((row) => String(effectiveStatus(row) || "").toLowerCase() === "failed").length;
    const creditsUsed = filtered.reduce((sum, row) => sum + Number(row?.creditsUsed || 0), 0);
    return { total, verified, failed, creditsUsed };
  }, [filtered]);

  useEffect(() => {
    setFilters((prev) => ({ ...prev, page: Math.min(prev.page, totalPages) }));
  }, [totalPages]);

  const exportRows = () => {
    downloadCsv("kyc-user-report.csv", [
      ["Session ID", "Provider", "Doc Type", "Status", "Customer Ref", "Credits Used", "Mobile", "Email", "Created At"],
      ...filtered.map((row) => [
        row?.sessionId || "",
        row?.provider || "",
        Array.isArray(row?.docTypes) ? row.docTypes.join(" | ") : "",
        normalizeStatus(effectiveStatus(row)).label,
        row?.customerRef || "",
        Number(row?.creditsUsed || 0),
        safeGet(row?.collected, "mobileFromXml", "") || safeGet(row?.collected, "mobileNumber", "") || safeGet(row?.collected, "mobile", ""),
        safeGet(row?.collected, "email", ""),
        formatDate(row?.createdAtUtc),
      ]),
    ]);
    toast.success("KYC report exported");
  };

  async function openRow(r) {
    setActive(r);
    setActiveDetail(null);
    setOpen(true);
    setPreviewUrl("");
    setActiveFileIndex(0);
    try {
      const detail = await getKycSession(r.sessionId);
      setActiveDetail(detail);
      const files = safeGet(detail?.result, "files", []);
      if (Array.isArray(files) && files.length > 0) {
        const idx = pickBestFileIndex(files, detail || r);
        setActiveFileIndex(idx);
        const f = files[idx];
        const url = decodeBase64ToBlobUrl(f?.fileBase64, f?.mime || "application/pdf");
        setPreviewUrl(url);
      }
    } catch (e) {
      toast.error(e?.message || "Failed to load KYC record");
    }
  }

  useEffect(() => {
    if (!open) return;
    const files = safeGet(activeDetail?.result, "files", []);
    if (!Array.isArray(files) || files.length === 0) return;
    if (activeFileIndex < 0 || activeFileIndex >= files.length) return;
    try {
      const f = files[activeFileIndex];
      const url = decodeBase64ToBlobUrl(f?.fileBase64, f?.mime || "application/pdf");
      setPreviewUrl((prev) => {
        if (prev) URL.revokeObjectURL(prev);
        return url;
      });
    } catch {
      // ignore
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeFileIndex, open]);

  return (
    <div className="w-full py-2">
      <div className="mb-4 grid gap-3 md:grid-cols-4">
        <Card className="rounded-3xl border-slate-200"><CardContent className="pt-5"><div className="text-xs uppercase text-slate-500">Sessions</div><div className="mt-2 text-2xl font-semibold text-slate-900">{summary.total}</div><div className="text-xs text-slate-500">Saved KYC records</div></CardContent></Card>
        <Card className="rounded-3xl border-slate-200"><CardContent className="pt-5"><div className="text-xs uppercase text-slate-500">Verified</div><div className="mt-2 text-2xl font-semibold text-emerald-700">{summary.verified}</div><div className="text-xs text-slate-500">Completed successfully</div></CardContent></Card>
        <Card className="rounded-3xl border-slate-200"><CardContent className="pt-5"><div className="text-xs uppercase text-slate-500">Failed</div><div className="mt-2 text-2xl font-semibold text-rose-700">{summary.failed}</div><div className="text-xs text-slate-500">Need review</div></CardContent></Card>
        <Card className="rounded-3xl border-slate-200"><CardContent className="pt-5"><div className="text-xs uppercase text-slate-500">Credits Used</div><div className="mt-2 text-2xl font-semibold text-slate-900">{summary.creditsUsed}</div><div className="text-xs text-slate-500">Total KYC credits used</div></CardContent></Card>
      </div>

      <Card className="rounded-3xl border-slate-200">
        <CardHeader className="flex flex-row items-start justify-between gap-4">
          <div>
            <CardTitle>KYC Reports</CardTitle>
            <CardDescription>Saved KYC sessions with customer reference, credit usage, extracted fields, and document previews.</CardDescription>
          </div>
          <div className="flex gap-2">
            <Button variant="outline" onClick={exportRows} disabled={!filtered.length}>Export CSV</Button>
            <Button className="bg-orange-500 hover:bg-orange-600" disabled={busy} onClick={refresh}>
              {busy ? "Loading..." : "Refresh"}
            </Button>
          </div>
        </CardHeader>
        <CardContent>
          <div className="mb-4 grid gap-4 md:grid-cols-6">
            <div className="space-y-2 md:col-span-2">
              <Label>Search</Label>
              <Input
                value={filters.q}
                onChange={(event) => setFilters((prev) => ({ ...prev, q: event.target.value, page: 1 }))}
                placeholder="Search session, customer ref, mobile, email"
              />
            </div>
            <div className="space-y-2">
              <Label>Status</Label>
              <Select value={filters.status} onValueChange={(value) => setFilters((prev) => ({ ...prev, status: value, page: 1 }))}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">All status</SelectItem>
                  <SelectItem value="verified">Success</SelectItem>
                  <SelectItem value="failed">Fail</SelectItem>
                  <SelectItem value="created">Pending</SelectItem>
                  <SelectItem value="redirected">Redirected</SelectItem>
                  <SelectItem value="expired">Expired</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-2">
              <Label>API</Label>
              <Select value={filters.api} onValueChange={(value) => setFilters((prev) => ({ ...prev, api: value, page: 1 }))}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">All APIs</SelectItem>
                  <SelectItem value="digilocker">DigiLocker</SelectItem>
                  <SelectItem value="gst">AppyFlow GST</SelectItem>
                  <SelectItem value="aadhaarxml">Aadhaar XML</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-2">
              <Label>From date</Label>
              <Input
                type="date"
                value={filters.fromDate}
                onChange={(event) => setFilters((prev) => ({ ...prev, fromDate: event.target.value, page: 1 }))}
              />
            </div>
            <div className="space-y-2">
              <Label>To date</Label>
              <Input
                type="date"
                value={filters.toDate}
                onChange={(event) => setFilters((prev) => ({ ...prev, toDate: event.target.value, page: 1 }))}
              />
            </div>
          </div>
          <div className="space-y-3 md:hidden">
            {pagedRows.length === 0 ? (
              <div className="rounded-2xl border border-dashed border-slate-200 px-4 py-10 text-center text-slate-500">
                No KYC records yet.
              </div>
            ) : (
              pagedRows.map((r, idx) => {
                const c = r.collected || {};
                const status = normalizeStatus(effectiveStatus(r));
                const doc = (Array.isArray(r.docTypes) && r.docTypes[0]) ? r.docTypes[0] : "-";
                return (
                  <div key={r.sessionId} className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
                    <div className="flex items-start justify-between gap-3">
                      <div>
                        <div className="text-xs uppercase tracking-[0.18em] text-slate-500">#{(filters.page - 1) * filters.pageSize + idx + 1}</div>
                        <div className="mt-1 text-lg font-semibold text-slate-900">{String(doc || "").toUpperCase()}</div>
                        <div className="text-xs text-slate-500">{formatDate(r.createdAtUtc)}</div>
                      </div>
                      <Badge className={status.className}>{status.label}</Badge>
                    </div>
                    <div className="mt-4 grid grid-cols-2 gap-3 text-sm">
                      <div>
                        <div className="text-xs uppercase text-slate-500">Customer Ref</div>
                        <div className="mt-1 font-medium text-slate-900 break-all">{r.customerRef || "-"}</div>
                      </div>
                      <div>
                        <div className="text-xs uppercase text-slate-500">Credits</div>
                        <div className="mt-1 font-medium text-slate-900">{Number(r.creditsUsed || 0)}</div>
                      </div>
                      <div>
                        <div className="text-xs uppercase text-slate-500">Mobile</div>
                        <div className="mt-1 font-medium text-slate-900 break-all">{safeGet(c, "mobile", "-")}</div>
                      </div>
                      <div>
                        <div className="text-xs uppercase text-slate-500">Email</div>
                        <div className="mt-1 font-medium text-slate-900 break-all">{safeGet(c, "email", "-")}</div>
                      </div>
                      <div className="col-span-2">
                        <div className="text-xs uppercase text-slate-500">Session ID</div>
                        <div className="mt-1 font-medium text-slate-900 break-all">{r.sessionId}</div>
                      </div>
                    </div>
                    <div className="mt-4 flex justify-end">
                      <Button variant="outline" className="rounded-xl" onClick={() => openRow(r)}>
                        View more
                      </Button>
                    </div>
                  </div>
                );
              })
            )}
          </div>
          <div className="hidden overflow-auto rounded-2xl border border-slate-200 md:block">
            <table className="min-w-full text-left text-sm">
              <thead className="sticky top-0 z-10 bg-slate-50 text-slate-600 shadow-sm">
                <tr>
                  <th className="px-4 py-3">S.No</th>
                  <th className="px-4 py-3">Doc</th>
                  <th className="px-4 py-3">Status</th>
                  <th className="px-4 py-3">Customer Ref</th>
                  <th className="px-4 py-3">Credits</th>
                  <th className="px-4 py-3">Session ID</th>
                  <th className="px-4 py-3">Mobile</th>
                  <th className="px-4 py-3">Email</th>
                  <th className="px-4 py-3">Created</th>
                  <th className="px-4 py-3"></th>
                </tr>
              </thead>
              <tbody>
                {pagedRows.length === 0 ? (
                  <tr>
                    <td colSpan={10} className="px-4 py-10 text-center text-slate-500">
                      No KYC records yet.
                    </td>
                  </tr>
                ) : (
                  pagedRows.map((r, idx) => {
                    const c = r.collected || {};
                    const status = normalizeStatus(effectiveStatus(r));
                    const doc = (Array.isArray(r.docTypes) && r.docTypes[0]) ? r.docTypes[0] : "-";
                    return (
                      <tr key={r.sessionId} className="border-t border-slate-200">
                        <td className="px-4 py-3">{(filters.page - 1) * filters.pageSize + idx + 1}</td>
                        <td className="px-4 py-3 font-medium text-slate-900">{String(doc || "").toUpperCase()}</td>
                        <td className="px-4 py-3">
                          <Badge className={status.className}>{status.label}</Badge>
                        </td>
                        <td className="px-4 py-3 text-slate-700">{r.customerRef || "-"}</td>
                        <td className="px-4 py-3 text-slate-700">{Number(r.creditsUsed || 0)}</td>
                        <td className="px-4 py-3 text-xs text-slate-700">{r.sessionId}</td>
                        <td className="px-4 py-3">{safeGet(c, "mobile", "-")}</td>
                        <td className="px-4 py-3">{safeGet(c, "email", "-")}</td>
                        <td className="px-4 py-3 text-slate-500">{formatDate(r.createdAtUtc)}</td>
                        <td className="px-4 py-3 text-right">
                          <Button variant="outline" className="rounded-xl" onClick={() => openRow(r)}>
                            View more
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
            <div className="text-sm text-slate-600">
              Showing {filtered.length ? ((filters.page - 1) * filters.pageSize) + 1 : 0}–{Math.min(filters.page * filters.pageSize, filtered.length)} of {filtered.length.toLocaleString()} records
            </div>
            <div className="flex items-center gap-2">
              <Select value={String(filters.pageSize)} onValueChange={(value) => setFilters((prev) => ({ ...prev, pageSize: Number(value), page: 1 }))}>
                <SelectTrigger className="h-9 w-[110px]"><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="10">10 / page</SelectItem>
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

      <Dialog open={open} onOpenChange={(v) => setOpen(v)}>
        <DialogContent className="h-[92vh] w-[96vw] max-w-[1500px] overflow-y-auto">
          <DialogHeader>
            <DialogTitle>KYC Record</DialogTitle>
          </DialogHeader>

          {!active ? null : (
            <div className="mb-4 grid gap-3 md:grid-cols-4">
              <Card className="border-slate-200"><CardContent className="pt-4"><div className="text-xs uppercase text-slate-500">Customer Ref</div><div className="mt-2 text-sm font-semibold text-slate-900">{activeDetail?.customerRef || active.customerRef || "-"}</div></CardContent></Card>
              <Card className="border-slate-200"><CardContent className="pt-4"><div className="text-xs uppercase text-slate-500">Credits Used</div><div className="mt-2 text-sm font-semibold text-slate-900">{Number(activeDetail?.creditsUsed || active?.creditsUsed || 0)}</div></CardContent></Card>
              <Card className="border-slate-200"><CardContent className="pt-4"><div className="text-xs uppercase text-slate-500">Created</div><div className="mt-2 text-sm font-semibold text-slate-900">{formatDate(activeDetail?.createdAtUtc || active.createdAtUtc)}</div></CardContent></Card>
              <Card className="border-slate-200"><CardContent className="pt-4"><div className="text-xs uppercase text-slate-500">Status</div><div className="mt-2 text-sm font-semibold text-slate-900"><Badge className={normalizeStatus(effectiveStatus(activeDetail || active)).className}>{normalizeStatus(effectiveStatus(activeDetail || active)).label}</Badge></div></CardContent></Card>
            </div>
          )}

          {prettifyFailureReason(effectiveFailureReason(activeDetail || active)) ? (
            <div className="mb-4 rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-800">
              <div className="text-xs font-semibold uppercase tracking-[0.18em] text-rose-600">Failure Reason</div>
              <div className="mt-1">{prettifyFailureReason(effectiveFailureReason(activeDetail || active))}</div>
            </div>
          ) : null}

          <div className="grid grid-cols-1 gap-4 md:grid-cols-[1.25fr_1fr]">
            <div className="rounded-2xl border border-slate-200 bg-slate-50 p-3">
              <div className="mb-2 flex items-center justify-between gap-3">
                <div className="text-xs font-medium text-slate-500">Document preview</div>
                {previewUrl ? (
                  <div className="flex items-center gap-2">
                    <Button
                      variant="outline"
                      className="h-7 rounded-lg px-2 text-xs"
                      onClick={() => {
                        const files = safeGet(activeDetail?.result, "files", []);
                        const f = Array.isArray(files) ? files[activeFileIndex] : null;
                        downloadBlobUrl(previewUrl, String(f?.fileName || "kyc-document.pdf"));
                      }}
                    >
                      Download
                    </Button>
                    <Button
                      variant="outline"
                      className="h-7 rounded-lg px-2 text-xs"
                      onClick={() => {
                        const popup = window.open(previewUrl, "_blank");
                        if (popup) {
                          popup.focus();
                          setTimeout(() => {
                            try { popup.print(); } catch {}
                          }, 400);
                        }
                      }}
                    >
                      Print
                    </Button>
                  </div>
                ) : null}
              </div>
              {previewUrl ? (
                <div className="relative h-[700px] w-full rounded-xl bg-white overflow-hidden">
                  <iframe title="kyc-preview" src={`${previewUrl}#toolbar=1&navpanes=0`} className="h-full w-full" />
                  {(() => {
                    const files = safeGet(activeDetail?.result, "files", []);
                    if (!Array.isArray(files) || files.length <= 1) return null;
                    const canPrev = activeFileIndex > 0;
                    const canNext = activeFileIndex < files.length - 1;
                    const f = files[activeFileIndex] || {};
                    const label = `${String(f?.doctype || "").toUpperCase() || "FILE"} (${activeFileIndex + 1}/${files.length})`;
                    return (
                      <div className="absolute left-3 right-3 top-3 flex items-center justify-between rounded-xl border border-slate-200 bg-white/90 px-3 py-2 text-xs text-slate-700 backdrop-blur">
                        <div className="font-medium">{label}</div>
                        <div className="flex items-center gap-2">
                          <Button variant="outline" className="h-7 rounded-lg px-2" disabled={!canPrev} onClick={() => setActiveFileIndex((i) => Math.max(0, i - 1))}>
                            Prev
                          </Button>
                          <Button variant="outline" className="h-7 rounded-lg px-2" disabled={!canNext} onClick={() => setActiveFileIndex((i) => Math.min(files.length - 1, i + 1))}>
                            Next
                          </Button>
                        </div>
                      </div>
                    );
                  })()}
                </div>
              ) : (
                <KycPreviewCard collected={activeDetail?.result?.collected || active?.collected || {}} active={activeDetail || active} />
              )}
            </div>

            <div className="rounded-2xl border border-slate-200 bg-white p-3">
              <div className="mb-2 text-xs font-medium text-slate-500">Extracted fields</div>

              {(() => {
                const errs = safeGet(activeDetail?.result, "fileDownloadErrors", []);
                if (!Array.isArray(errs) || errs.length === 0) return null;
                const hasInsufficientScope = errs.some((e) => {
                  const code = String(e?.errorCode || "").toUpperCase();
                  const msg = String(e?.error || e?.message || e?.error_description || "").toLowerCase();
                  return code === "DIGILOCKER_INSUFFICIENT_SCOPE" || msg.includes("insufficient_scope");
                });
                return (
                  <div className="mb-3 rounded-2xl border border-amber-200 bg-amber-50 p-3">
                    <div className="flex items-start justify-between gap-3">
                      <div>
                        <div className="text-sm font-semibold text-amber-900">Some DigiLocker downloads were blocked</div>
                        <div className="mt-1 text-xs text-amber-900/80">
                          {hasInsufficientScope
                            ? "e-Aadhaar XML needs higher privileges/scopes for this DigiLocker partner. This record is generated from available profile + issued-doc metadata."
                            : "This record is generated from available profile + issued-doc metadata."}
                        </div>
                      </div>
                      <Button
                        variant="outline"
                        className="h-8 rounded-xl border-amber-300 bg-white px-3 text-amber-900 hover:bg-amber-100"
                        onClick={() => navigate("/dashboard/platform-settings?tab=digilocker")}
                      >
                        Open DigiLocker settings
                      </Button>
                    </div>
                    <div className="mt-2 space-y-1 text-xs text-amber-900/80">
                      {errs.slice(0, 3).map((e, idx) => {
                        const uri = String(e?.uri || "-");
                        const dt = String(e?.doctype || "-");
                        const status = e?.status ? ` status=${e.status}` : "";
                        const msg = String(e?.error || e?.message || "Download failed.");
                        return (
                          <div key={`${uri}-${idx}`} className="rounded-xl bg-white/60 px-2 py-1">
                            <span className="font-medium">{dt}</span> <span className="text-amber-900/70">{uri}{status}</span>: {msg}
                          </div>
                        );
                      })}
                      {errs.length > 3 ? <div className="text-amber-900/70">+{errs.length - 3} more</div> : null}
                    </div>
                  </div>
                );
              })()}

              {(() => {
                const collected = activeDetail?.result?.collected || active?.collected || {};
                const aadhaarNo = safeGet(collected, "aadhaarNumberFull", "") || safeGet(collected, "aadhaarNumber", "") || "-";
                const referenceId = safeGet(collected, "referenceId", "-");
                const photo = toDataUrl(collected.photoBase64);
                const mobile = safeGet(collected, "mobileFromXml", "-");
                const enteredMobile = safeGet(collected, "mobileNumber", "-");
                const fatherName = safeGet(collected, "fatherName", "") || safeGet(collected, "careOfRaw", "-");
                const signature = activeDetail?.result?.signature || active?.signature || {};
                const mobileVerification = activeDetail?.result?.mobileVerification || {};
                const signatureOk = String(safeGet(signature, "valid", "")).toLowerCase() === "true";
                const uidaiCert = String(safeGet(signature, "uidaiCertificate", "")).toLowerCase() === "true";
                const isAadhaar = isAadhaarProvider(activeDetail || active);
                const verificationUrl = safeGet(activeDetail?.result?.trail, "verificationUrl", "") || safeGet(activeDetail?.result?.source, "verificationUrl", "");
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
                        <div className="text-slate-500">Father / Guardian</div>
                        <div className="font-medium text-slate-900">{fatherName}</div>
                      </div>
                      <div>
                        <div className="text-slate-500">XML Mobile Hash</div>
                        <div className="font-medium text-slate-900">{mobile}</div>
                      </div>
                      <div>
                        <div className="text-slate-500">Entered Mobile</div>
                        <div className="font-medium text-slate-900">{enteredMobile}</div>
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
                      <div className="col-span-2">
                        <div className="text-slate-500">Reference ID</div>
                        <div className="font-medium text-slate-900">{referenceId}</div>
                      </div>
                      <div className="col-span-2">
                        <div className="text-slate-500">Mobile Hash Matched</div>
                        <div className="font-medium text-slate-900">{String(safeGet(mobileVerification, "matched", "-"))}</div>
                      </div>
                      {isAadhaar ? <div className="col-span-2 rounded-2xl border border-slate-200 bg-slate-50 p-4">
                        <div className="mb-3 flex items-center justify-between gap-3">
                          <div>
                            <div className="text-xs font-medium uppercase tracking-wide text-slate-500">Digital Signature</div>
                            <div className="text-sm font-semibold text-slate-900">
                              {signatureOk && uidaiCert ? "UIDAI signature verified" : "Signature review required"}
                            </div>
                          </div>
                          <div className="flex gap-2">
                            <Badge className={signatureOk ? "bg-emerald-600 hover:bg-emerald-600" : "bg-rose-600 hover:bg-rose-600"}>
                              {signatureOk ? "Valid" : "Invalid"}
                            </Badge>
                            <Badge variant="outline" className={uidaiCert ? "border-emerald-300 text-emerald-700" : "border-amber-300 text-amber-700"}>
                              {uidaiCert ? "UIDAI cert" : "Cert review"}
                            </Badge>
                          </div>
                        </div>
                        {verificationUrl ? (
                          <a
                            href={verificationUrl}
                            target="_blank"
                            rel="noreferrer"
                            className="mb-3 inline-flex rounded-lg border border-blue-200 bg-blue-50 px-3 py-1 text-xs font-medium text-blue-700 hover:bg-blue-100"
                          >
                            Open signature verification
                          </a>
                        ) : null}
                        <div className="grid grid-cols-2 gap-3 text-sm">
                          <div>
                            <div className="text-slate-500">Signature Valid</div>
                            <div className="font-medium text-slate-900">{String(safeGet(signature, "valid", "-"))}</div>
                          </div>
                          <div>
                            <div className="text-slate-500">UIDAI Certificate</div>
                            <div className="font-medium text-slate-900">{String(safeGet(signature, "uidaiCertificate", "-"))}</div>
                          </div>
                          <div className="col-span-2">
                            <div className="text-slate-500">Certificate Subject</div>
                            <div className="break-all font-medium text-slate-900">{safeGet(signature, "certificateSubject", "-")}</div>
                          </div>
                          <div className="col-span-2">
                            <div className="text-slate-500">Certificate Issuer</div>
                            <div className="break-all font-medium text-slate-900">{safeGet(signature, "certificateIssuer", "-")}</div>
                          </div>
                          <div>
                            <div className="text-slate-500">Signing Algorithm</div>
                            <div className="font-medium text-slate-900">{safeGet(signature, "signingAlgorithm", "-")}</div>
                          </div>
                          <div>
                            <div className="text-slate-500">Digest Algorithm</div>
                            <div className="font-medium text-slate-900">{safeGet(signature, "digestAlgorithm", "-")}</div>
                          </div>
                        </div>
                      </div> : null}
                    </div>
                  </div>
                );
              })()}

              <div className="mt-4 rounded-2xl border border-slate-200 bg-slate-50 p-3">
                {renderAttributeGroups(safeGet(activeDetail?.result?.collected, "rawAttributes", null) || safeGet(active?.collected, "rawAttributes", null))}
                {safeGet(activeDetail?.result?.source, "rawXmlDecoded", "") ? (
                  <div className="mb-3">
                    <div className="mb-2 text-xs font-medium text-slate-500">Decoded XML</div>
                    <pre className="max-h-[180px] overflow-auto rounded-xl bg-white p-3 text-xs text-slate-700">
                      {safeGet(activeDetail?.result?.source, "rawXmlDecoded", "")}
                    </pre>
                  </div>
                ) : null}
                {safeGet(activeDetail?.result?.source, "rawXmlBase64", "") ? (
                  <div className="mb-3">
                    <div className="mb-2 text-xs font-medium text-slate-500">XML Base64</div>
                    <pre className="max-h-[120px] overflow-auto rounded-xl bg-white p-3 text-xs text-slate-700">
                      {safeGet(activeDetail?.result?.source, "rawXmlBase64", "")}
                    </pre>
                  </div>
                ) : null}
                <div className="mb-2 text-xs font-medium text-slate-500">Received response</div>
                <pre className="max-h-[220px] overflow-auto rounded-xl bg-white p-3 text-xs text-slate-700">
                  {formatResponse(activeDetail?.result || active?.result || {})}
                </pre>
              </div>
            </div>
          </div>
        </DialogContent>
      </Dialog>
    </div>
  );
}
