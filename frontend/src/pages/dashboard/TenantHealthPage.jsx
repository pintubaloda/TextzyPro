import { useEffect, useMemo, useState } from "react";
import { getTenantHealth } from "@/lib/api";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { toast } from "sonner";

function pct(value) {
  const n = Number(value || 0);
  if (!Number.isFinite(n)) return "-";
  return `${n.toFixed(2)}%`;
}

function ms(value) {
  const n = Number(value || 0);
  if (!Number.isFinite(n)) return "-";
  if (n >= 1000) return `${(n / 1000).toFixed(2)}s`;
  return `${n.toFixed(0)}ms`;
}

function scoreColor(score) {
  const s = Number(score || 0);
  if (s >= 90) return "border border-emerald-200 bg-emerald-100 text-emerald-700";
  if (s >= 80) return "border border-lime-200 bg-lime-100 text-lime-700";
  if (s >= 65) return "border border-amber-200 bg-amber-100 text-amber-700";
  if (s >= 50) return "border border-orange-200 bg-orange-100 text-orange-700";
  return "border border-rose-200 bg-rose-100 text-rose-700";
}

export default function TenantHealthPage() {
  const [days, setDays] = useState("7");
  const [busy, setBusy] = useState(false);
  const [data, setData] = useState(null);

  const refresh = async () => {
    try {
      setBusy(true);
      const res = await getTenantHealth({ days: Number(days || 7) });
      setData(res || null);
      toast.success("Health refreshed");
    } catch (e) {
      toast.error(e?.message || "Failed to load health");
    } finally {
      setBusy(false);
    }
  };

  useEffect(() => {
    refresh();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [days]);

  const score = data?.score?.score ?? 0;
  const reasons = Array.isArray(data?.score?.reasons) ? data.score.reasons : [];

  const creditBalances = useMemo(() => {
    const raw = data?.billing?.creditBalances;
    if (!raw || typeof raw !== "object") return [];
    return Object.entries(raw).map(([k, v]) => ({ k, v: Number(v || 0) }));
  }, [data]);

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-2 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <h1 className="text-2xl font-heading font-bold text-slate-900">Tenant Health</h1>
          <p className="text-slate-600">Delivery rate, failures, webhook lag, KYC success, and billing status.</p>
        </div>
        <div className="flex items-center gap-2">
          <Select value={days} onValueChange={setDays}>
            <SelectTrigger className="w-28"><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value="7">7 days</SelectItem>
              <SelectItem value="14">14 days</SelectItem>
              <SelectItem value="30">30 days</SelectItem>
            </SelectContent>
          </Select>
          <Button variant="outline" onClick={refresh} disabled={busy}>{busy ? "Loading..." : "Refresh"}</Button>
        </div>
      </div>

      <div className="grid gap-4 md:grid-cols-3">
        <Card className="border-slate-200 shadow-sm">
          <CardHeader>
            <CardTitle>Score</CardTitle>
            <CardDescription>Overall operational score</CardDescription>
          </CardHeader>
          <CardContent className="space-y-2">
            <div className="flex items-center gap-2">
              <Badge className={scoreColor(score)}>{score} / 100</Badge>
              {data?.score?.grade ? <Badge variant="outline">Grade {data.score.grade}</Badge> : null}
            </div>
            {reasons.length ? (
              <ul className="text-sm text-slate-700 list-disc pl-5 space-y-1">
                {reasons.map((r) => <li key={r}>{r}</li>)}
              </ul>
            ) : (
              <p className="text-sm text-slate-600">No issues detected in this window.</p>
            )}
          </CardContent>
        </Card>

        <Card className="border-slate-200 shadow-sm">
          <CardHeader>
            <CardTitle>WhatsApp</CardTitle>
            <CardDescription>Outbound delivery snapshot</CardDescription>
          </CardHeader>
          <CardContent className="space-y-1 text-sm">
            <div className="flex justify-between"><span className="text-slate-500">Delivery rate</span><span className="font-medium">{pct(data?.whatsapp?.deliveryRatePct)}</span></div>
            <div className="flex justify-between"><span className="text-slate-500">Total</span><span className="font-medium">{data?.whatsapp?.outboundTotal ?? 0}</span></div>
            <div className="flex justify-between"><span className="text-slate-500">Success</span><span className="font-medium">{data?.whatsapp?.outboundSuccess ?? 0}</span></div>
            <div className="flex justify-between"><span className="text-slate-500">Failed</span><span className="font-medium">{data?.whatsapp?.outboundFailed ?? 0}</span></div>
            <div className="flex justify-between"><span className="text-slate-500">Queued</span><span className="font-medium">{data?.whatsapp?.outboundQueued ?? 0}</span></div>
          </CardContent>
        </Card>

        <Card className="border-slate-200 shadow-sm">
          <CardHeader>
            <CardTitle>Webhooks</CardTitle>
            <CardDescription>Meta webhook lag</CardDescription>
          </CardHeader>
          <CardContent className="space-y-1 text-sm">
            <div className="flex justify-between"><span className="text-slate-500">p95</span><span className="font-medium">{ms(data?.webhook?.p95Ms)}</span></div>
            <div className="flex justify-between"><span className="text-slate-500">p99</span><span className="font-medium">{ms(data?.webhook?.p99Ms)}</span></div>
            <div className="flex justify-between"><span className="text-slate-500">Pending</span><span className="font-medium">{data?.webhook?.pending ?? 0}</span></div>
            <div className="flex justify-between"><span className="text-slate-500">Oldest pending</span><span className="font-medium">{data?.webhook?.oldestPendingAgeSec ?? 0}s</span></div>
          </CardContent>
        </Card>
      </div>

      <div className="grid gap-4 md:grid-cols-2">
        <Card className="border-slate-200 shadow-sm">
          <CardHeader>
            <CardTitle>KYC</CardTitle>
            <CardDescription>Verification success snapshot</CardDescription>
          </CardHeader>
          <CardContent className="space-y-1 text-sm">
            <div className="flex justify-between"><span className="text-slate-500">Success rate</span><span className="font-medium">{pct(data?.kyc?.successRatePct)}</span></div>
            <div className="flex justify-between"><span className="text-slate-500">Total</span><span className="font-medium">{data?.kyc?.total ?? 0}</span></div>
            <div className="flex justify-between"><span className="text-slate-500">Verified</span><span className="font-medium">{data?.kyc?.verified ?? 0}</span></div>
            <div className="flex justify-between"><span className="text-slate-500">Failed</span><span className="font-medium">{data?.kyc?.failed ?? 0}</span></div>
            <div className="flex justify-between"><span className="text-slate-500">Pending</span><span className="font-medium">{data?.kyc?.pending ?? 0}</span></div>
          </CardContent>
        </Card>

        <Card className="border-slate-200 shadow-sm">
          <CardHeader>
            <CardTitle>Billing</CardTitle>
            <CardDescription>Subscription and balances</CardDescription>
          </CardHeader>
          <CardContent className="space-y-2 text-sm">
            <div className="flex justify-between"><span className="text-slate-500">Subscription</span><span className="font-medium">{data?.billing?.subscription?.status || "-"}</span></div>
            <div className="flex justify-between"><span className="text-slate-500">Plan</span><span className="font-medium">{data?.billing?.plan?.name || "-"}</span></div>
            <div className="pt-2 border-t border-slate-100">
              <div className="text-xs font-medium text-slate-500 mb-2">Credit balances</div>
              {creditBalances.length ? (
                <div className="grid gap-1">
                  {creditBalances.map((row) => (
                    <div key={row.k} className="flex justify-between">
                      <span className="text-slate-500">{row.k}</span>
                      <span className="font-medium">{row.v}</span>
                    </div>
                  ))}
                </div>
              ) : (
                <div className="text-slate-600">No credit balances.</div>
              )}
            </div>
          </CardContent>
        </Card>
      </div>
    </div>
  );
}

