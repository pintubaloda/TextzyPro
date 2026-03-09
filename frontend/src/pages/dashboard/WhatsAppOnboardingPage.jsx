import { useCallback, useEffect, useMemo, useState } from "react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { CheckCircle2, AlertCircle, Loader2, RefreshCw } from "lucide-react";
import { toast } from "sonner";
import { Input } from "@/components/ui/input";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import {
  wabaStartOnboarding,
  wabaGetOnboardingStatus,
  wabaGetEmbeddedConfig,
  wabaExchangeCode,
  wabaMapExisting,
  wabaRecheckOnboarding,
  wabaDisconnectOnboarding,
  authProjects,
  getSession,
} from "@/lib/api";
import { loadFacebookSdk } from "@/lib/facebookSdk";

const stateMap = {
  requested: "Requested",
  code_received: "Code Received",
  exchanged: "Exchanged",
  assets_linked: "Assets Linked",
  webhook_subscribed: "Webhook Subscribed",
  verified: "Verified",
  ready: "Connected / Ready",
};

export default function WhatsAppOnboardingPage() {
  const [loading, setLoading] = useState(true);
  const [starting, setStarting] = useState(false);
  const [connecting, setConnecting] = useState(false);
  const [checking, setChecking] = useState(false);
  const [mappingExisting, setMappingExisting] = useState(false);
  const [mapDialogOpen, setMapDialogOpen] = useState(false);
  const [projects, setProjects] = useState([]);
  const [mapForm, setMapForm] = useState({
    tenantId: "",
    wabaId: "",
    phoneNumberId: "",
    accessToken: "",
  });
  const [status, setStatus] = useState({ state: "requested", readyToSend: false, isConnected: false });
  const [embeddedCfg, setEmbeddedCfg] = useState({
    appId: process.env.REACT_APP_FACEBOOK_APP_ID || "",
    configId: process.env.REACT_APP_WABA_EMBEDDED_CONFIG_ID || "",
  });

  const stateLabel = useMemo(() => stateMap[status.state] || status.state || "Requested", [status.state]);
  const fmt = (v) => (v ? new Date(v).toLocaleString() : "—");

  async function loadStatus(force = false) {
    setLoading(true);
    try {
      const payload = await wabaGetOnboardingStatus({ force });
      setStatus(payload || {});
    } catch (e) {
      toast.error(e.message || "Failed to load onboarding status");
    } finally {
      setLoading(false);
    }
  }

  const ensureEmbeddedConfig = useCallback(async () => {
    if (embeddedCfg.appId && embeddedCfg.configId) return;
    try {
      const cfg = await wabaGetEmbeddedConfig();
      if (cfg?.appId || cfg?.embeddedConfigId) {
        setEmbeddedCfg((prev) => ({
          appId: prev.appId || (cfg.appId || ""),
          configId: prev.configId || (cfg.embeddedConfigId || ""),
        }));
      }
    } catch {
      // Keep env-based values only.
    }
  }, [embeddedCfg.appId, embeddedCfg.configId]);

  useEffect(() => {
    loadStatus(false);
    ensureEmbeddedConfig();
  }, [ensureEmbeddedConfig]);

  async function resolveEmbeddedConfig() {
    let appId = embeddedCfg.appId;
    let configId = embeddedCfg.configId;
    if (appId && configId) return { appId, configId };
    try {
      const cfg = await wabaGetEmbeddedConfig();
      appId = appId || (cfg?.appId || "");
      configId = configId || (cfg?.embeddedConfigId || "");
      setEmbeddedCfg({ appId, configId });
    } catch {
      // noop
    }
    return { appId, configId };
  }

  async function handleStart() {
    setStarting(true);
    try {
      await wabaStartOnboarding();
      await loadStatus(true);
      toast.success("Onboarding started");
    } catch (e) {
      toast.error(e.message || "Failed to start onboarding");
    } finally {
      setStarting(false);
    }
  }

  async function handleEmbeddedSignup() {
    const { appId: facebookAppId, configId: embeddedConfigId } = await resolveEmbeddedConfig();
    if (!facebookAppId || !embeddedConfigId) {
      toast.error("Missing Facebook App ID or Embedded Config ID in Platform WABA Master Config");
      return;
    }

    setConnecting(true);
    try {
      const FB = await loadFacebookSdk(facebookAppId);
      FB.login((response) => {
        if (!response || !response.authResponse) {
          setConnecting(false);
          toast.error("Embedded signup cancelled");
          return;
        }

        const code = response.authResponse.code;
        if (!code) {
          setConnecting(false);
          toast.error("Meta did not return authorization code. Strict code-only exchange is enforced.");
          return;
        }

        Promise.resolve()
          .then(() => wabaExchangeCode(code))
          .then(() => loadStatus(true))
          .then(() => toast.success("Embedded signup exchange complete"))
          .catch((e) => {
            toast.error(e?.message || "Code exchange failed");
          })
          .finally(() => setConnecting(false));
      }, {
        config_id: embeddedConfigId,
        response_type: "code",
        override_default_response_type: true,
        scope: "business_management,whatsapp_business_management,whatsapp_business_messaging",
      });
    } catch {
      setConnecting(false);
      toast.error("Failed to load Facebook SDK");
    }
  }

  async function handleRecheck() {
    setChecking(true);
    try {
      const payload = await wabaRecheckOnboarding();
      setStatus(payload || {});
      toast.success("Checks refreshed");
    } catch (e) {
      toast.error(e.message || "Failed to refresh checks");
    } finally {
      setChecking(false);
    }
  }

  async function handleDisconnect() {
    const ok = window.confirm("Disconnect this project's WABA now?");
    if (!ok) return;
    try {
      await wabaDisconnectOnboarding();
      await loadStatus(true);
      toast.success("WABA disconnected for this project");
    } catch (e) {
      toast.error(e?.message || "Failed to disconnect WABA");
    }
  }

  async function handleMapExistingWaba() {
    try {
      const list = await authProjects();
      const projectRows = Array.isArray(list) ? list : [];
      const session = getSession();
      const selected = projectRows.find((x) => x.slug === session?.tenantSlug) || projectRows[0];
      setProjects(projectRows);
      setMapForm((prev) => ({
        ...prev,
        tenantId: selected?.id || prev.tenantId || "",
      }));
      setMapDialogOpen(true);
    } catch (e) {
      toast.error(e?.message || "Failed to load projects");
    }
  }

  async function submitMapExisting() {
    if (!mapForm.tenantId) {
      toast.error("Select project");
      return;
    }
    if (!mapForm.wabaId.trim() || !mapForm.phoneNumberId.trim() || !mapForm.accessToken.trim()) {
      toast.error("Project, WABA ID, Phone Number ID and Access Token are required");
      return;
    }

    setMappingExisting(true);
    try {
      await wabaMapExisting({
        tenantId: mapForm.tenantId,
        wabaId: mapForm.wabaId.trim(),
        phoneNumberId: mapForm.phoneNumberId.trim(),
        accessToken: mapForm.accessToken.trim(),
      });
      setMapDialogOpen(false);
      setMapForm((prev) => ({ ...prev, wabaId: "", phoneNumberId: "", accessToken: "" }));
      await loadStatus(true);
      toast.success("Existing WABA mapped to selected project");
    } catch (e) {
      toast.error(e?.message || "Failed to map existing WABA");
    } finally {
      setMappingExisting(false);
    }
  }

  return (
    <div className="space-y-6" data-testid="whatsapp-onboarding-page">
      <div className="flex flex-col md:flex-row md:items-center md:justify-between gap-4">
        <div>
          <h1 className="text-2xl font-heading font-bold text-slate-900">WhatsApp Embedded Signup</h1>
          <p className="text-slate-600">Tech Partner onboarding with strict authorization-code exchange</p>
        </div>
        <div className="flex gap-2">
          <Button variant="outline" onClick={handleRecheck} disabled={checking || loading}>
            {checking ? <Loader2 className="w-4 h-4 mr-2 animate-spin" /> : <RefreshCw className="w-4 h-4 mr-2" />}Recheck
          </Button>
          <Button onClick={handleStart} disabled={starting || loading} className="bg-orange-500 hover:bg-orange-600 text-white">
            {starting ? <Loader2 className="w-4 h-4 mr-2 animate-spin" /> : null}Start Onboarding
          </Button>
        </div>
      </div>

      <Card className="border-slate-200">
        <CardHeader>
          <div className="flex items-center justify-between">
            <div>
              <CardTitle>Onboarding Status</CardTitle>
              <CardDescription>Live state progression and readiness checks</CardDescription>
            </div>
            <Badge className={status.readyToSend ? "bg-green-100 text-green-700 hover:bg-green-100" : "bg-amber-100 text-amber-700 hover:bg-amber-100"}>
              {status.readyToSend ? "Ready to Send" : stateLabel}
            </Badge>
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="grid md:grid-cols-2 gap-4 text-sm">
            <div className="p-3 rounded-lg bg-slate-50"><b>Business:</b> {status.businessName || "Not linked"}</div>
            <div className="p-3 rounded-lg bg-slate-50"><b>Phone:</b> {status.phone || "Not linked"}</div>
            <div className="p-3 rounded-lg bg-slate-50"><b>WABA ID:</b> {status.wabaId || "Pending"}</div>
            <div className="p-3 rounded-lg bg-slate-50"><b>Phone Number ID:</b> {status.phoneNumberId || "Pending"}</div>
            <div className="p-3 rounded-lg bg-slate-50"><b>Business Manager ID:</b> {status.businessManagerId || "Pending"}</div>
            <div className="p-3 rounded-lg bg-slate-50"><b>System User:</b> {status.systemUserName || status.systemUserId || "Pending"}</div>
            <div className="p-3 rounded-lg bg-slate-50"><b>Token Source:</b> {status.tokenSource || "exchanged_token"}</div>
            <div className="p-3 rounded-lg bg-slate-50"><b>Permanent Token Issued:</b> {fmt(status.permanentTokenIssuedAtUtc)}</div>
            <div className="p-3 rounded-lg bg-slate-50"><b>Business Verification:</b> {status.businessVerificationStatus || "Unknown"}</div>
            <div className="p-3 rounded-lg bg-slate-50"><b>Quality / Name:</b> {status.phoneQualityRating || "Unknown"} / {status.phoneNameStatus || "Unknown"}</div>
          </div>

          <div className="rounded-lg border border-slate-200 p-3">
            <p className="text-sm font-medium text-slate-900 mb-2">Onboarding Timeline</p>
            <div className="grid md:grid-cols-2 gap-2 text-xs">
              <div className="rounded border border-slate-100 bg-slate-50 p-2"><b>Requested:</b> {fmt(status.timeline?.requestedAtUtc)}</div>
              <div className="rounded border border-slate-100 bg-slate-50 p-2"><b>Code received:</b> {fmt(status.timeline?.codeReceivedAtUtc)}</div>
              <div className="rounded border border-slate-100 bg-slate-50 p-2"><b>Exchanged:</b> {fmt(status.timeline?.exchangedAtUtc)}</div>
              <div className="rounded border border-slate-100 bg-slate-50 p-2"><b>Assets linked:</b> {fmt(status.timeline?.assetsLinkedAtUtc)}</div>
              <div className="rounded border border-slate-100 bg-slate-50 p-2"><b>Webhook subscribed:</b> {fmt(status.timeline?.webhookSubscribedAtUtc)}</div>
              <div className="rounded border border-slate-100 bg-slate-50 p-2"><b>Verified:</b> {fmt(status.timeline?.verifiedAtUtc)}</div>
            </div>
          </div>

          <div className="flex items-center gap-2 text-sm">
            {status.permissionAuditPassed ? <CheckCircle2 className="w-4 h-4 text-green-600" /> : <AlertCircle className="w-4 h-4 text-amber-600" />}
            Permission Audit: {status.permissionAuditPassed ? "Passed" : "Missing required scopes"}
          </div>
          <div className="flex items-center gap-2 text-sm">
            {status.webhookSubscribed ? <CheckCircle2 className="w-4 h-4 text-green-600" /> : <AlertCircle className="w-4 h-4 text-amber-600" />}
            Webhook Subscription: {status.webhookSubscribed ? "Active" : "Not verified"}
          </div>

          {status.lastError ? (
            <Alert className="border-amber-200 bg-amber-50">
              <AlertCircle className="h-4 w-4" />
              <AlertTitle>Onboarding Warning</AlertTitle>
              <AlertDescription>{status.lastError}</AlertDescription>
            </Alert>
          ) : null}

          {status.lastGraphError ? (
            <Alert className="border-red-200 bg-red-50">
              <AlertCircle className="h-4 w-4" />
              <AlertTitle>Graph Error Payload</AlertTitle>
              <AlertDescription className="break-all">{status.lastGraphError}</AlertDescription>
            </Alert>
          ) : null}

          <div className="pt-2">
            <div className="flex flex-wrap gap-2">
              <Button onClick={handleEmbeddedSignup} disabled={connecting || loading} className="bg-orange-500 hover:bg-orange-600 text-white">
                {connecting ? <Loader2 className="w-4 h-4 mr-2 animate-spin" /> : null}
                Continue with Facebook
              </Button>
              <Button onClick={handleMapExistingWaba} disabled={connecting || loading} className="bg-orange-500 hover:bg-orange-600 text-white">
                {connecting ? <Loader2 className="w-4 h-4 mr-2 animate-spin" /> : null}
                Map Existing WABA
              </Button>
              {status?.isConnected || status?.readyToSend || String(status?.state || "").toLowerCase() === "ready" ? (
                <Button variant="outline" className="text-red-600 border-red-200 hover:bg-red-50" onClick={handleDisconnect}>
                  Disconnect WABA
                </Button>
              ) : null}
            </div>
            <p className="text-sm text-slate-500 mt-2">
              If this WhatsApp Business is already onboarded with your app, use <b>Map Existing WABA</b> to attach it to this project.
            </p>
          </div>
        </CardContent>
      </Card>

      <Dialog open={mapDialogOpen} onOpenChange={setMapDialogOpen}>
        <DialogContent className="sm:max-w-xl">
          <DialogHeader>
            <DialogTitle>Map Existing WABA</DialogTitle>
            <DialogDescription>
              Enter existing WhatsApp Cloud credentials and map them to a project.
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-3">
            <div>
              <label className="text-sm font-medium text-slate-700">Select Project</label>
              <select
                className="mt-1 h-10 w-full rounded-md border border-slate-200 bg-white px-3 text-sm"
                value={mapForm.tenantId}
                onChange={(e) => setMapForm((prev) => ({ ...prev, tenantId: e.target.value }))}
              >
                <option value="">Select project</option>
                {projects.map((p) => (
                  <option key={p.id} value={p.id}>
                    {p.name}
                  </option>
                ))}
              </select>
            </div>
            <div>
              <label className="text-sm font-medium text-slate-700">WABA ID</label>
              <Input
                placeholder="Enter WABA ID"
                value={mapForm.wabaId}
                onChange={(e) => setMapForm((prev) => ({ ...prev, wabaId: e.target.value }))}
              />
            </div>
            <div>
              <label className="text-sm font-medium text-slate-700">Phone Number ID</label>
              <Input
                placeholder="Enter Phone Number ID"
                value={mapForm.phoneNumberId}
                onChange={(e) => setMapForm((prev) => ({ ...prev, phoneNumberId: e.target.value }))}
              />
            </div>
            <div>
              <label className="text-sm font-medium text-slate-700">Access Token</label>
              <Input
                type="password"
                placeholder="Enter Access Token"
                value={mapForm.accessToken}
                onChange={(e) => setMapForm((prev) => ({ ...prev, accessToken: e.target.value }))}
              />
            </div>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setMapDialogOpen(false)} disabled={mappingExisting}>
              Cancel
            </Button>
            <Button
              className="bg-orange-500 hover:bg-orange-600 text-white"
              onClick={submitMapExisting}
              disabled={mappingExisting}
            >
              {mappingExisting ? <Loader2 className="w-4 h-4 mr-2 animate-spin" /> : null}
              Map Existing WABA
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
