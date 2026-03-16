import { useEffect, useMemo, useState } from "react";
import { listKycSessions, getKycSession } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Badge } from "@/components/ui/badge";
import { toast } from "sonner";
import { useBranding } from "@/hooks/useBranding";

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

function KycPreviewCard({ brand, collected, active }) {
  const name = safeGet(collected, "name", "-");
  const dob = safeGet(collected, "dob", "-");
  const gender = safeGet(collected, "gender", "-");
  const fatherName = safeGet(collected, "fatherName", "-");
  const aadhaar = safeGet(collected, "aadhaarMasked", "") || (String(safeGet(collected, "aadhaarVerified", "")).toLowerCase() === "true" ? "Verified" : "-");
  const pan = safeGet(collected, "pan", "-");
  const address = safeGet(collected, "address", "-");
  const photo = toDataUrl(collected.photoBase64);
  const docType = (Array.isArray(active?.docTypes) && active.docTypes[0]) ? String(active.docTypes[0]).toUpperCase() : "KYC";

  return (
    <div className="relative h-[540px] w-full overflow-hidden rounded-xl bg-white">
      <div className="absolute inset-0 pointer-events-none flex items-center justify-center opacity-[0.08]">
        {brand?.logoUrl ? (
          <img src={brand.logoUrl} alt={brand.name || "Textzy"} className="h-36 w-36 object-contain" />
        ) : (
          <div className="text-6xl font-black tracking-tight text-slate-900">{brand?.name || "Textzy"}</div>
        )}
      </div>

      <div className="relative p-5">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-3">
            {brand?.logoUrl ? (
              <img src={brand.logoUrl} alt={brand.name || "Textzy"} className="h-10 w-10 rounded-xl border border-slate-200 object-cover" />
            ) : (
              <div className="flex h-10 w-10 items-center justify-center rounded-xl border border-slate-200 bg-slate-50 text-sm font-bold text-slate-900">
                {String(brand?.name || "Textzy").slice(0, 1).toUpperCase()}
              </div>
            )}
            <div>
              <div className="text-sm font-semibold text-slate-900">{brand?.name || "Textzy"}</div>
              <div className="text-xs text-slate-500">{docType} KYC Record</div>
            </div>
          </div>
          <Badge className="bg-emerald-600 hover:bg-emerald-600">Verified</Badge>
        </div>

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
                <div className="text-slate-500">Father Name</div>
                <div className="font-semibold text-slate-900">{fatherName}</div>
              </div>
              <div>
                <div className="text-slate-500">Aadhaar</div>
                <div className="font-semibold text-slate-900">{aadhaar}</div>
              </div>
              <div>
                <div className="text-slate-500">PAN</div>
                <div className="font-semibold text-slate-900">{pan}</div>
              </div>
              <div className="col-span-2">
                <div className="text-slate-500">Address</div>
                <div className="font-semibold text-slate-900">{address}</div>
              </div>
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
            <div className="text-[11px] text-slate-500">Powered by {brand?.name || "Textzy"}</div>
          </div>
        </div>

        <div className="mt-3 text-[11px] text-slate-500">
          Note: If DigiLocker blocks PDF/XML download, this preview is generated from available profile and issued-doc metadata.
        </div>
      </div>
    </div>
  );
}

export default function KycReportsPage() {
  const { brand } = useBranding();
  const [rows, setRows] = useState([]);
  const [busy, setBusy] = useState(false);
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(null);
  const [activeDetail, setActiveDetail] = useState(null);
  const [previewUrl, setPreviewUrl] = useState("");
  const [activeFileIndex, setActiveFileIndex] = useState(0);

  function mapRequestedToDoctype(req) {
    const r = String(req || "").trim().toUpperCase();
    if (!r) return "";
    if (r === "PAN") return "PANCR";
    if (r === "DL" || r === "DRIVING_LICENCE" || r === "DRIVINGLICENSE" || r === "DRIVING-LICENCE") return "DRVLC";
    if (r === "AADHAAR" || r === "AADHAR") return "ADHAR";
    return r;
  }

  function pickBestFileIndex(files, record) {
    if (!Array.isArray(files) || files.length === 0) return 0;
    const requested = (Array.isArray(record?.docTypes) && record.docTypes[0]) ? String(record.docTypes[0]) : "";
    const want = mapRequestedToDoctype(requested);
    if (want) {
      const idx = files.findIndex((f) => String(f?.doctype || "").toUpperCase() === want);
      if (idx >= 0) return idx;
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
                <div className="relative h-[540px] w-full rounded-xl bg-white overflow-hidden">
                  <iframe title="kyc-preview" src={previewUrl} className="h-full w-full" />
                  <div className="pointer-events-none absolute inset-0 flex items-center justify-center opacity-[0.08]">
                    {brand?.logoUrl ? (
                      <img src={brand.logoUrl} alt={brand.name || "Textzy"} className="h-36 w-36 object-contain" />
                    ) : (
                      <div className="text-6xl font-black tracking-tight text-slate-900">{brand?.name || "Textzy"}</div>
                    )}
                  </div>
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
                <KycPreviewCard brand={brand} collected={activeDetail?.result?.collected || active?.collected || {}} active={activeDetail || active} />
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
                        <div className="text-slate-500">Aadhaar</div>
                        <div className="font-medium text-slate-900">
                          {safeGet(collected, "aadhaarMasked", "") ||
                            (String(safeGet(collected, "aadhaarVerified", "")).toLowerCase() === "true" ? "Verified (no number)" : "-")}
                        </div>
                      </div>
                      <div className="col-span-2">
                        <div className="text-slate-500">Father Name</div>
                        <div className="font-medium text-slate-900">{safeGet(collected, "fatherName", "-")}</div>
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
