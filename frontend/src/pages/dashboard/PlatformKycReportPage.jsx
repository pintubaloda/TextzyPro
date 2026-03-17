import { useEffect, useMemo, useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { toast } from "sonner";
import { getPlatformCustomers, getPlatformKycReport } from "@/lib/api";

const formatDate = (value) => {
  if (!value) return "-";
  try {
    return new Date(value).toLocaleString();
  } catch {
    return String(value);
  }
};

const statusBadge = (status) => {
  const s = String(status || "").toLowerCase();
  if (s === "verified") return "bg-emerald-100 text-emerald-700";
  if (s === "failed") return "bg-rose-100 text-rose-700";
  if (s === "expired") return "bg-amber-100 text-amber-700";
  return "bg-slate-100 text-slate-700";
};

function decodeBase64ToBlobUrl(base64, mime = "application/pdf") {
  const b = String(base64 || "").trim();
  if (!b) return "";
  const binary = atob(b);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  const blob = new Blob([bytes], { type: mime || "application/octet-stream" });
  return URL.createObjectURL(blob);
}

export default function PlatformKycReportPage() {
  const [rows, setRows] = useState([]);
  const [tenants, setTenants] = useState([]);
  const [loading, setLoading] = useState(false);
  const [filters, setFilters] = useState({
    tenantId: "",
    status: "all",
    q: "",
    fromUtc: "",
    toUtc: "",
    take: 100,
    skip: 0,
  });
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(null);
  const [previewUrl, setPreviewUrl] = useState("");
  const [activeFileIndex, setActiveFileIndex] = useState(0);

  const load = async (patch = {}) => {
    const next = { ...filters, ...patch };
    setFilters(next);
    setLoading(true);
    try {
      const [customerRows, report] = await Promise.all([
        getPlatformCustomers("").catch(() => []),
        getPlatformKycReport({
          tenantId: next.tenantId,
          status: next.status === "all" ? "" : next.status,
          q: next.q,
          fromUtc: next.fromUtc,
          toUtc: next.toUtc,
          take: next.take,
          skip: next.skip,
        }),
      ]);
      setTenants(Array.isArray(customerRows) ? customerRows : []);
      setRows(Array.isArray(report?.items) ? report.items : []);
    } catch (e) {
      toast.error(e?.message || "Failed to load KYC report");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    return () => {
      if (previewUrl) URL.revokeObjectURL(previewUrl);
    };
  }, [previewUrl]);

  const files = useMemo(() => {
    if (!active?.result?.documents) return [];
    return Array.isArray(active.result.documents) ? active.result.documents : [];
  }, [active]);

  const activeFile = files[activeFileIndex] || null;

  useEffect(() => {
    if (!activeFile?.fileBase64) {
      if (previewUrl) URL.revokeObjectURL(previewUrl);
      setPreviewUrl("");
      return;
    }
    const url = decodeBase64ToBlobUrl(activeFile.fileBase64, activeFile.mime);
    if (previewUrl) URL.revokeObjectURL(previewUrl);
    setPreviewUrl(url);
    return () => {
      if (url) URL.revokeObjectURL(url);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeFile?.fileBase64, activeFile?.mime]);

  return (
    <div className="space-y-4">
      <Card className="border-slate-200">
        <CardHeader>
          <CardTitle>KYC Platform Report</CardTitle>
          <CardDescription>Every DigiLocker/GST session with full response, user, and webhook status.</CardDescription>
        </CardHeader>
        <CardContent className="grid gap-4 md:grid-cols-4">
          <div className="space-y-2">
            <Label>Tenant</Label>
            <Select value={filters.tenantId || "all"} onValueChange={(value) => load({ tenantId: value === "all" ? "" : value })}>
              <SelectTrigger><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="all">All tenants</SelectItem>
                {tenants.map((t) => (
                  <SelectItem key={t.tenantId} value={t.tenantId}>{t.companyName || t.slug || t.tenantId}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="space-y-2">
            <Label>Status</Label>
            <Select value={filters.status} onValueChange={(value) => load({ status: value })}>
              <SelectTrigger><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="all">All</SelectItem>
                <SelectItem value="created">Created</SelectItem>
                <SelectItem value="redirected">Redirected</SelectItem>
                <SelectItem value="verified">Verified</SelectItem>
                <SelectItem value="failed">Failed</SelectItem>
                <SelectItem value="expired">Expired</SelectItem>
              </SelectContent>
            </Select>
          </div>
          <div className="space-y-2">
            <Label>From (UTC)</Label>
            <Input placeholder="2026-03-01T00:00:00Z" value={filters.fromUtc} onChange={(e) => setFilters((p) => ({ ...p, fromUtc: e.target.value }))} />
          </div>
          <div className="space-y-2">
            <Label>To (UTC)</Label>
            <Input placeholder="2026-03-31T23:59:59Z" value={filters.toUtc} onChange={(e) => setFilters((p) => ({ ...p, toUtc: e.target.value }))} />
          </div>
          <div className="space-y-2 md:col-span-3">
            <Label>Search (session id or customerRef)</Label>
            <Input value={filters.q} onChange={(e) => setFilters((p) => ({ ...p, q: e.target.value }))} placeholder="session id / customer reference" />
          </div>
          <div className="flex items-end gap-2">
            <Button className="bg-orange-500 hover:bg-orange-600" disabled={loading} onClick={() => load()}>Apply</Button>
            <Button variant="outline" disabled={loading} onClick={() => load({ q: "", fromUtc: "", toUtc: "", status: "all", tenantId: "" })}>Reset</Button>
          </div>
        </CardContent>
      </Card>

      <Card className="border-slate-200">
        <CardHeader className="border-b border-slate-100 bg-slate-50/70">
          <CardTitle>Sessions</CardTitle>
          <CardDescription>Click view to inspect full DigiLocker response, documents, and webhook status.</CardDescription>
        </CardHeader>
        <CardContent className="p-0">
          <div className="overflow-x-auto">
            <table className="w-full min-w-[1200px] text-sm">
              <thead className="bg-slate-50">
                <tr>
                  <th className="px-4 py-3 text-left font-semibold text-slate-600">S.No</th>
                  <th className="px-4 py-3 text-left font-semibold text-slate-600">Date</th>
                  <th className="px-4 py-3 text-left font-semibold text-slate-600">User</th>
                  <th className="px-4 py-3 text-left font-semibold text-slate-600">Session Id</th>
                  <th className="px-4 py-3 text-left font-semibold text-slate-600">Customer Ref</th>
                  <th className="px-4 py-3 text-left font-semibold text-slate-600">Doc Type</th>
                  <th className="px-4 py-3 text-left font-semibold text-slate-600">Status</th>
                  <th className="px-4 py-3 text-left font-semibold text-slate-600">Webhook</th>
                  <th className="px-4 py-3 text-left font-semibold text-slate-600">View</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row, index) => (
                  <tr key={row.sessionId} className="border-t border-slate-100">
                    <td className="px-4 py-3 text-slate-600">{index + 1}</td>
                    <td className="px-4 py-3 text-slate-600">{formatDate(row.createdAtUtc)}</td>
                    <td className="px-4 py-3">
                      <div className="text-slate-900">{row.userEmail || "public"}</div>
                      <div className="text-xs text-slate-500">{row.tenantName || row.tenantSlug}</div>
                    </td>
                    <td className="px-4 py-3 text-slate-600">{row.sessionId}</td>
                    <td className="px-4 py-3 text-slate-600">{row.customerRef || "-"}</td>
                    <td className="px-4 py-3 text-slate-600">{Array.isArray(row.docTypes) ? row.docTypes.join(", ") : "-"}</td>
                    <td className="px-4 py-3">
                      <Badge className={statusBadge(row.status)}>{row.status || "-"}</Badge>
                    </td>
                    <td className="px-4 py-3 text-slate-600">
                      {row.webhook
                        ? `${row.webhook.statusCode} (${row.webhook.ok ? "ok" : "failed"})`
                        : "-"}
                    </td>
                    <td className="px-4 py-3">
                      <Button variant="outline" size="sm" onClick={() => {
                        setActive(row);
                        setActiveFileIndex(0);
                        setOpen(true);
                      }}>View</Button>
                    </td>
                  </tr>
                ))}
                {rows.length === 0 ? (
                  <tr>
                    <td colSpan={9} className="px-4 py-10 text-center text-slate-500">No KYC sessions found.</td>
                  </tr>
                ) : null}
              </tbody>
            </table>
          </div>
        </CardContent>
      </Card>

      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent className="max-w-5xl">
          <DialogHeader>
            <DialogTitle>KYC Session Detail</DialogTitle>
          </DialogHeader>
          {!active ? null : (
            <div className="grid gap-4 md:grid-cols-[1.2fr_1fr]">
              <div className="space-y-4">
                <div className="rounded-xl border border-slate-200 p-4 text-sm">
                  <div className="grid gap-2 md:grid-cols-2">
                    <div><span className="text-slate-500">Session</span><div className="font-medium">{active.sessionId}</div></div>
                    <div><span className="text-slate-500">Customer Ref</span><div className="font-medium">{active.customerRef || "-"}</div></div>
                    <div><span className="text-slate-500">User</span><div className="font-medium">{active.userEmail || "public"}</div></div>
                    <div><span className="text-slate-500">Tenant</span><div className="font-medium">{active.tenantName || active.tenantSlug}</div></div>
                    <div><span className="text-slate-500">Status</span><div className="font-medium">{active.status}</div></div>
                    <div><span className="text-slate-500">Doc Types</span><div className="font-medium">{Array.isArray(active.docTypes) ? active.docTypes.join(", ") : "-"}</div></div>
                  </div>
                </div>

                <Tabs defaultValue="public">
                  <TabsList>
                    <TabsTrigger value="public">Public Response</TabsTrigger>
                    <TabsTrigger value="raw">Raw DigiLocker Response</TabsTrigger>
                  </TabsList>
                  <TabsContent value="public">
                    <pre className="max-h-[320px] overflow-auto rounded-xl border border-slate-200 bg-slate-50 p-3 text-xs">
                      {JSON.stringify(active.result || {}, null, 2)}
                    </pre>
                  </TabsContent>
                  <TabsContent value="raw">
                    <pre className="max-h-[320px] overflow-auto rounded-xl border border-slate-200 bg-slate-50 p-3 text-xs">
                      {active.rawResultJson || "{}"}
                    </pre>
                  </TabsContent>
                </Tabs>
              </div>

              <div className="space-y-4">
                <div className="rounded-xl border border-slate-200 p-4 text-sm">
                  <div className="flex items-center justify-between">
                    <div className="font-medium text-slate-900">Documents</div>
                    <Badge variant="outline">{files.length} files</Badge>
                  </div>
                  {files.length === 0 ? (
                    <div className="mt-3 text-slate-500">No documents in this session.</div>
                  ) : (
                    <div className="mt-3 space-y-2">
                      {files.map((f, idx) => (
                        <button
                          key={`${f.uri}-${idx}`}
                          type="button"
                          className={`w-full rounded-lg border px-3 py-2 text-left text-xs ${idx === activeFileIndex ? "border-orange-300 bg-orange-50" : "border-slate-200"}`}
                          onClick={() => setActiveFileIndex(idx)}
                        >
                          <div className="font-medium text-slate-900">{f.name || f.uri}</div>
                          <div className="text-slate-500">{f.doctype} · {f.mime} · {(Number(f.sizeBytes || 0) / 1024).toFixed(1)} KB</div>
                        </button>
                      ))}
                    </div>
                  )}
                </div>
                {previewUrl ? (
                  <iframe title="doc-preview" src={previewUrl} className="h-[420px] w-full rounded-xl border border-slate-200" />
                ) : (
                  <div className="flex h-[420px] items-center justify-center rounded-xl border border-dashed border-slate-200 text-sm text-slate-500">
                    Select a document to preview.
                  </div>
                )}
              </div>
            </div>
          )}
        </DialogContent>
      </Dialog>
    </div>
  );
}
