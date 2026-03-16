import { useEffect, useMemo, useState } from "react";
import { listKycSessions, getKycSession } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Badge } from "@/components/ui/badge";
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
  if (s === "verified") return { label: "Verified", variant: "default" };
  if (s === "failed") return { label: "Failed", variant: "destructive" };
  if (s) return { label: s, variant: "secondary" };
  return { label: "-", variant: "secondary" };
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

export default function KycReportsPage() {
  const [rows, setRows] = useState([]);
  const [busy, setBusy] = useState(false);
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(null);
  const [activeDetail, setActiveDetail] = useState(null);
  const [previewUrl, setPreviewUrl] = useState("");

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

  async function openRow(r) {
    setActive(r);
    setActiveDetail(null);
    setOpen(true);
    setPreviewUrl("");
    try {
      const detail = await getKycSession(r.sessionId);
      setActiveDetail(detail);
      const files = safeGet(detail?.result, "files", []);
      if (Array.isArray(files) && files.length > 0) {
        const f = files[0];
        const url = decodeBase64ToBlobUrl(f?.fileBase64, f?.mime || "application/pdf");
        setPreviewUrl(url);
      }
    } catch (e) {
      toast.error(e?.message || "Failed to load KYC record");
    }
  }

  return (
    <div className="mx-auto w-full max-w-6xl px-4 py-6">
      <Card className="rounded-3xl border-slate-200">
        <CardHeader className="flex flex-row items-start justify-between gap-4">
          <div>
            <CardTitle>KYC Reports</CardTitle>
            <CardDescription>Saved KYC sessions with extracted fields (PAN/Aadhaar) and document previews.</CardDescription>
          </div>
          <Button className="bg-orange-500 hover:bg-orange-600" disabled={busy} onClick={refresh}>
            {busy ? "Loading..." : "Refresh"}
          </Button>
        </CardHeader>
        <CardContent>
          <div className="overflow-auto rounded-2xl border border-slate-200">
            <table className="min-w-full text-left text-sm">
              <thead className="bg-slate-50 text-slate-600">
                <tr>
                  <th className="px-4 py-3">Doc</th>
                  <th className="px-4 py-3">Status</th>
                  <th className="px-4 py-3">Name</th>
                  <th className="px-4 py-3">DOB</th>
                  <th className="px-4 py-3">Gender</th>
                  <th className="px-4 py-3">Created</th>
                  <th className="px-4 py-3"></th>
                </tr>
              </thead>
              <tbody>
                {sorted.length === 0 ? (
                  <tr>
                    <td colSpan={7} className="px-4 py-10 text-center text-slate-500">
                      No KYC records yet.
                    </td>
                  </tr>
                ) : (
                  sorted.map((r) => {
                    const c = r.collected || {};
                    const status = normalizeStatus(r.status);
                    const doc = (Array.isArray(r.docTypes) && r.docTypes[0]) ? r.docTypes[0] : "-";
                    return (
                      <tr key={r.sessionId} className="border-t border-slate-200">
                        <td className="px-4 py-3 font-medium text-slate-900">{String(doc || "").toUpperCase()}</td>
                        <td className="px-4 py-3">
                          <Badge variant={status.variant}>{status.label}</Badge>
                        </td>
                        <td className="px-4 py-3">{safeGet(c, "name", "-")}</td>
                        <td className="px-4 py-3">{safeGet(c, "dob", "-")}</td>
                        <td className="px-4 py-3">{safeGet(c, "gender", "-")}</td>
                        <td className="px-4 py-3 text-slate-500">{String(r.createdAtUtc || "").replace("T", " ").replace("Z", "")}</td>
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
        </CardContent>
      </Card>

      <Dialog open={open} onOpenChange={(v) => setOpen(v)}>
        <DialogContent className="max-w-5xl">
          <DialogHeader>
            <DialogTitle>KYC Record</DialogTitle>
          </DialogHeader>

          <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
            <div className="rounded-2xl border border-slate-200 bg-slate-50 p-3">
              <div className="mb-2 text-xs font-medium text-slate-500">Document preview</div>
              {previewUrl ? (
                <iframe title="kyc-preview" src={previewUrl} className="h-[540px] w-full rounded-xl bg-white" />
              ) : (
                <div className="flex h-[540px] items-center justify-center rounded-xl bg-white text-sm text-slate-500">
                  No preview available
                </div>
              )}
            </div>

            <div className="rounded-2xl border border-slate-200 bg-white p-3">
              <div className="mb-2 text-xs font-medium text-slate-500">Extracted fields</div>

              {(() => {
                const collected = activeDetail?.result?.collected || active?.collected || {};
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
                      <div>
                        <div className="text-slate-500">Name</div>
                        <div className="font-medium text-slate-900">{safeGet(collected, "name", "-")}</div>
                      </div>
                      <div>
                        <div className="text-slate-500">DOB</div>
                        <div className="font-medium text-slate-900">{safeGet(collected, "dob", "-")}</div>
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
                        <div className="text-slate-500">Aadhaar</div>
                        <div className="font-medium text-slate-900">{safeGet(collected, "aadhaarMasked", "-")}</div>
                      </div>
                      <div className="col-span-2">
                        <div className="text-slate-500">Address</div>
                        <div className="font-medium text-slate-900">{safeGet(collected, "address", "-")}</div>
                      </div>
                    </div>
                  </div>
                );
              })()}

              <div className="mt-4 rounded-2xl border border-slate-200 bg-slate-50 p-3">
                <div className="text-xs font-medium text-slate-500">Raw (debug)</div>
                <pre className="mt-2 max-h-56 overflow-auto rounded-xl bg-white p-3 text-xs text-slate-800">
                  {JSON.stringify(activeDetail || active, null, 2)}
                </pre>
              </div>
            </div>
          </div>
        </DialogContent>
      </Dialog>
    </div>
  );
}

