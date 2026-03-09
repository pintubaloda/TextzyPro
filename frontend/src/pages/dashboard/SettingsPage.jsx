import { useCallback, useEffect, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { Textarea } from "@/components/ui/textarea";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Tabs, TabsContent } from "@/components/ui/tabs";
import { Avatar, AvatarFallback, AvatarImage } from "@/components/ui/avatar";
import { Globe, Phone, Upload, Save, MessageSquare, Instagram, ChevronRight, ExternalLink, Loader2, CheckCircle2, AlertCircle } from "lucide-react";
import { toast } from "sonner";
import { getCompanySettings, getNotificationSettings, getSecurityOverview, getSession, saveCompanySettings, saveNotificationSettings, wabaExchangeCode, wabaGetEmbeddedConfig, wabaGetOnboardingStatus, wabaRecheckOnboarding, wabaStartOnboarding } from "@/lib/api";
import { loadFacebookSdk } from "@/lib/facebookSdk";

const SettingsPage = () => {
  const notifyBroadcastRef = useState(() => {
    try {
      if (typeof BroadcastChannel !== "undefined") return new BroadcastChannel("textzy_inbox_notifications");
    } catch {
      // ignore
    }
    return null;
  })[0];
  const [saving, setSaving] = useState(false);
  const [searchParams, setSearchParams] = useSearchParams();
  const [activeTab, setActiveTab] = useState(searchParams.get("tab") || "profile");
  const [wabaView, setWabaView] = useState("whatsapp");
  const [whatsappStatus, setWhatsappStatus] = useState({ state: "requested", readyToSend: false, isConnected: false });
  const [startingWaba, setStartingWaba] = useState(false);
  const [connectingWaba, setConnectingWaba] = useState(false);
  const [checkingWaba, setCheckingWaba] = useState(false);
  const [embeddedCfg, setEmbeddedCfg] = useState({
    appId: process.env.REACT_APP_FACEBOOK_APP_ID || "",
    configId: process.env.REACT_APP_WABA_EMBEDDED_CONFIG_ID || "",
  });
  const [company, setCompany] = useState({
    companyName: "",
    legalName: "",
    industry: "",
    website: "",
    companySize: "",
    address: "",
    gstin: "",
    pan: "",
    billingEmail: "",
    billingPhone: "",
    taxRatePercent: 18,
    isTaxExempt: false,
    isReverseCharge: false,
    isActive: true,
  });
  const [notifyPrefs, setNotifyPrefs] = useState({
    desktopEnabled: true,
    soundEnabled: true,
    soundStyle: "whatsapp",
    soundVolume: 1,
    inAppNewMessages: true,
    inAppSystemAlerts: true,
    dndUntilUtc: null,
  });
  const [notifyUpdatedAtUtc, setNotifyUpdatedAtUtc] = useState(null);
  const [securityOverview, setSecurityOverview] = useState({
    authenticator: { enabled: false, provider: "", enrolledAtUtc: "" },
    currentSession: null,
    recentSessions: [],
    sessionRetention: { note: "" },
  });
  const session = getSession() || {};
  const displayName = String(session.fullName || session.name || session.email || "User").trim();
  const [firstName, ...restNames] = displayName.split(" ");
  const lastName = restNames.join(" ").trim();
  const profileInitials = `${(firstName || "U").charAt(0)}${(lastName || "").charAt(0)}`.toUpperCase();
  const fmt = (v) => (v ? new Date(v).toLocaleString() : "â€”");
  const whatsappConnected = !!whatsappStatus?.isConnected
    || !!whatsappStatus?.readyToSend
    || String(whatsappStatus?.state || "").toLowerCase() === "ready";
  const whatsappReady = !!whatsappStatus?.readyToSend
    || String(whatsappStatus?.state || "").toLowerCase() === "ready";

  const handleSave = async () => {
    if (activeTab !== "company") {
      if (activeTab === "notifications") {
        setSaving(true);
        try {
          const saved = await saveNotificationSettings({
            desktopEnabled: !!notifyPrefs.desktopEnabled,
            soundEnabled: !!notifyPrefs.soundEnabled,
            soundStyle: notifyPrefs.soundStyle || "whatsapp",
            soundVolume: Number(notifyPrefs.soundVolume || 1),
            inAppNewMessages: !!notifyPrefs.inAppNewMessages,
            inAppSystemAlerts: !!notifyPrefs.inAppSystemAlerts,
            dndUntilUtc: notifyPrefs.dndUntilUtc || null,
          });
          setNotifyPrefs({
            desktopEnabled: !!saved?.desktopEnabled,
            soundEnabled: !!saved?.soundEnabled,
            soundStyle: saved?.soundStyle || "whatsapp",
            soundVolume: Number(saved?.soundVolume ?? 1),
            inAppNewMessages: !!saved?.inAppNewMessages,
            inAppSystemAlerts: !!saved?.inAppSystemAlerts,
            dndUntilUtc: saved?.dndUntilUtc || null,
          });
          setNotifyUpdatedAtUtc(saved?.updatedAtUtc || null);
          try {
            notifyBroadcastRef?.postMessage?.({
              type: "notify_settings_updated",
              payload: {
                desktopEnabled: !!saved?.desktopEnabled,
                soundEnabled: !!saved?.soundEnabled,
                soundStyle: saved?.soundStyle || "whatsapp",
                soundVolume: Number(saved?.soundVolume ?? 1),
                inAppNewMessages: !!saved?.inAppNewMessages,
                inAppSystemAlerts: !!saved?.inAppSystemAlerts,
                dndUntilUtc: saved?.dndUntilUtc || null,
                updatedAtUtc: saved?.updatedAtUtc || null,
              },
            });
          } catch {
            // ignore
          }
          toast.success("Notification settings saved.");
        } catch (e) {
          toast.error(e.message || "Failed to save notification settings.");
        } finally {
          setSaving(false);
        }
        return;
      }
      if (activeTab === "profile" || activeTab === "security") {
        toast.info("This section is informational right now. Use dedicated auth flows for account changes.");
        return;
      }
      toast.info("This section does not use the global save action.");
      return;
    }
    setSaving(true);
    try {
      const saved = await saveCompanySettings(company);
      setCompany({
        companyName: saved?.companyName || "",
        legalName: saved?.legalName || "",
        industry: saved?.industry || "",
        website: saved?.website || "",
        companySize: saved?.companySize || "",
        address: saved?.address || "",
        gstin: saved?.gstin || "",
        pan: saved?.pan || "",
        billingEmail: saved?.billingEmail || "",
        billingPhone: saved?.billingPhone || "",
        taxRatePercent: Number(saved?.taxRatePercent ?? 18),
        isTaxExempt: !!saved?.isTaxExempt,
        isReverseCharge: !!saved?.isReverseCharge,
        isActive: saved?.isActive ?? true,
      });
      toast.success("Company settings saved.");
    } catch (e) {
      toast.error(e.message || "Failed to save company settings.");
    } finally {
      setSaving(false);
    }
  };

  useEffect(() => {
    const tab = searchParams.get("tab");
    if (tab && tab !== activeTab) setActiveTab(tab);
  }, [searchParams, activeTab]);

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
    if (activeTab === "whatsapp") {
      loadWabaStatus(true);
      ensureEmbeddedConfig();
    }
    if (activeTab === "company") {
      getCompanySettings()
        .then((data) => {
          setCompany({
            companyName: data?.companyName || "",
            legalName: data?.legalName || "",
            industry: data?.industry || "",
            website: data?.website || "",
            companySize: data?.companySize || "",
            address: data?.address || "",
            gstin: data?.gstin || "",
            pan: data?.pan || "",
            billingEmail: data?.billingEmail || "",
            billingPhone: data?.billingPhone || "",
            taxRatePercent: Number(data?.taxRatePercent ?? 18),
            isTaxExempt: !!data?.isTaxExempt,
            isReverseCharge: !!data?.isReverseCharge,
            isActive: data?.isActive ?? true,
          });
        })
        .catch(() => {});
    }
    if (activeTab === "notifications") {
      getNotificationSettings()
        .then((data) => {
          setNotifyPrefs({
            desktopEnabled: !!data?.desktopEnabled,
            soundEnabled: !!data?.soundEnabled,
            soundStyle: data?.soundStyle || "whatsapp",
            soundVolume: Number(data?.soundVolume ?? 1),
            inAppNewMessages: !!data?.inAppNewMessages,
            inAppSystemAlerts: !!data?.inAppSystemAlerts,
            dndUntilUtc: data?.dndUntilUtc || null,
          });
          setNotifyUpdatedAtUtc(data?.updatedAtUtc || null);
        })
        .catch(() => {});
    }
    if (activeTab === "security") {
      getSecurityOverview()
        .then((data) => {
          setSecurityOverview(data || {
            authenticator: { enabled: false, provider: "", enrolledAtUtc: "" },
            currentSession: null,
            recentSessions: [],
            sessionRetention: { note: "" },
          });
        })
        .catch(() => {});
    }
  }, [activeTab, ensureEmbeddedConfig]);

  useEffect(() => {
    return () => {
      try {
        notifyBroadcastRef?.close?.();
      } catch {
        // ignore
      }
    };
  }, [notifyBroadcastRef]);

  const resolveEmbeddedConfig = async () => {
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
  };

  const authenticatorEnabled = !!securityOverview?.authenticator?.enabled;
  const authenticatorProvider = securityOverview?.authenticator?.provider
    ? String(securityOverview.authenticator.provider).replaceAll("_", " ")
    : "";
  const recentSessions = Array.isArray(securityOverview?.recentSessions) ? securityOverview.recentSessions : [];

  const loadWabaStatus = async (force = false) => {
    try {
      const data = await wabaGetOnboardingStatus({ force });
      setWhatsappStatus(data || {});
    } catch {
      setWhatsappStatus({ state: "requested", readyToSend: false, isConnected: false });
    }
  };


  const handleWabaStart = async () => {
    setStartingWaba(true);
    try {
      await wabaStartOnboarding();
      await loadWabaStatus(true);
      toast.success("Onboarding started");
    } catch (e) {
      toast.error(e.message || "Failed to start onboarding");
    } finally {
      setStartingWaba(false);
    }
  };

  const handleWabaRecheck = async () => {
    setCheckingWaba(true);
    try {
      const data = await wabaRecheckOnboarding();
      setWhatsappStatus(data || {});
      toast.success("Status refreshed");
    } catch (e) {
      toast.error(e.message || "Failed to refresh status");
    } finally {
      setCheckingWaba(false);
    }
  };

  const handleWabaConnect = async () => {
    const { appId: facebookAppId, configId: embeddedConfigId } = await resolveEmbeddedConfig();
    if (!facebookAppId || !embeddedConfigId) {
      toast.error("Missing Facebook App ID or Embedded Config ID in Platform WABA Master Config");
      return;
    }

    setConnectingWaba(true);
    try {
      const FB = await loadFacebookSdk(facebookAppId);

      FB.login((response) => {
        if (!response || !response.authResponse) {
          setConnectingWaba(false);
          toast.error("Embedded signup cancelled");
          return;
        }
        const code = response.authResponse.code;
        if (!code) {
          setConnectingWaba(false);
          toast.error("Meta did not return authorization code");
          return;
        }
        Promise.resolve()
          .then(() => wabaExchangeCode(code))
          .then(() => loadWabaStatus(true))
          .then(() => toast.success("WhatsApp connected"))
          .catch((e) => {
            toast.error(e?.message || "Code exchange failed");
          })
          .finally(() => setConnectingWaba(false));
      }, {
        config_id: embeddedConfigId,
        response_type: "code",
        override_default_response_type: true,
        scope: "business_management,whatsapp_business_management,whatsapp_business_messaging",
      });
    } catch {
      setConnectingWaba(false);
      toast.error("Failed to load Facebook SDK");
    }
  };

  return (
    <div className="space-y-6" data-testid="settings-page">
      {/* Header */}
      <div className="flex flex-col md:flex-row md:items-center md:justify-between gap-4">
        <div>
          <h1 className="text-2xl font-heading font-bold text-slate-900">Settings</h1>
          <p className="text-slate-600">Manage your account and preferences</p>
        </div>
        <Button
          className="bg-orange-500 hover:bg-orange-600 text-white gap-2"
          onClick={handleSave}
          disabled={saving}
          data-testid="save-settings-btn"
        >
          <Save className="w-4 h-4" />
          {saving ? "Saving..." : "Save Changes"}
        </Button>
      </div>

      <Tabs
        value={activeTab}
        onValueChange={(value) => {
          setActiveTab(value);
          setSearchParams({ tab: value });
        }}
        className="space-y-6"
      >
        {/* Profile Tab */}
        <TabsContent value="profile">
          <Card className="border-slate-200">
            <CardHeader>
              <CardTitle>Profile Information</CardTitle>
              <CardDescription>Update your personal details</CardDescription>
            </CardHeader>
            <CardContent className="space-y-6">
              <div className="flex items-center gap-6">
                <Avatar className="w-24 h-24">
                  <AvatarImage src="" />
                  <AvatarFallback className="bg-orange-100 text-orange-600 text-2xl font-medium">{profileInitials || "U"}</AvatarFallback>
                </Avatar>
                <div>
                  <div className="inline-flex items-center gap-2 rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-700">
                    <Upload className="w-4 h-4" />
                    Profile editing is account-managed
                  </div>
                  <p className="text-sm text-slate-500 mt-2">This screen currently shows authenticated user identity from your active session.</p>
                </div>
              </div>

              <div className="grid md:grid-cols-2 gap-6">
                <div className="space-y-2">
                  <Label>First Name</Label>
                  <Input value={firstName} readOnly data-testid="first-name-input" />
                </div>
                <div className="space-y-2">
                  <Label>Last Name</Label>
                  <Input value={lastName} readOnly data-testid="last-name-input" />
                </div>
                <div className="space-y-2">
                  <Label>Email Address</Label>
                  <Input type="email" value={session.email || ""} readOnly data-testid="email-input" />
                </div>
                <div className="space-y-2">
                  <Label>Phone Number</Label>
                  <Input value={session.phone || ""} readOnly placeholder="Not available" data-testid="phone-input" />
                </div>
                <div className="space-y-2">
                  <Label>Role</Label>
                  <Input value={session.role || ""} disabled />
                </div>
                <div className="space-y-2">
                  <Label>Project</Label>
                  <Input value={session.projectName || session.tenantSlug || ""} readOnly data-testid="timezone-select" />
                </div>
              </div>
            </CardContent>
          </Card>
        </TabsContent>

        {/* Company Tab */}
        <TabsContent value="company">
          <Card className="border-slate-200">
            <CardHeader>
              <CardTitle>Company Information</CardTitle>
              <CardDescription>Update your business details. Tax profile is managed by the platform owner.</CardDescription>
            </CardHeader>
            <CardContent className="space-y-6">
              <div className="grid md:grid-cols-2 gap-6">
                <div className="space-y-2">
                  <Label>Company Name</Label>
                  <Input value={company.companyName} onChange={(e) => setCompany((p) => ({ ...p, companyName: e.target.value }))} data-testid="company-name-input" />
                </div>
                <div className="space-y-2">
                  <Label>Legal Name</Label>
                  <Input value={company.legalName} onChange={(e) => setCompany((p) => ({ ...p, legalName: e.target.value }))} data-testid="legal-name-input" />
                </div>
                <div className="space-y-2">
                  <Label>Industry</Label>
                  <Select value={company.industry || "none"} onValueChange={(v) => setCompany((p) => ({ ...p, industry: v === "none" ? "" : v }))}>
                    <SelectTrigger data-testid="industry-select">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="none">Select industry</SelectItem>
                      <SelectItem value="technology">Technology</SelectItem>
                      <SelectItem value="ecommerce">E-commerce</SelectItem>
                      <SelectItem value="healthcare">Healthcare</SelectItem>
                      <SelectItem value="finance">Finance</SelectItem>
                    </SelectContent>
                  </Select>
                </div>
                <div className="space-y-2">
                  <Label>Website</Label>
                  <Input value={company.website} onChange={(e) => setCompany((p) => ({ ...p, website: e.target.value }))} data-testid="website-input" />
                </div>
                <div className="space-y-2">
                  <Label>Company Size</Label>
                  <Select value={company.companySize || "none"} onValueChange={(v) => setCompany((p) => ({ ...p, companySize: v === "none" ? "" : v }))}>
                    <SelectTrigger data-testid="company-size-select">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="none">Select size</SelectItem>
                      <SelectItem value="1-10">1-10 employees</SelectItem>
                      <SelectItem value="11-50">11-50 employees</SelectItem>
                      <SelectItem value="50-100">50-100 employees</SelectItem>
                      <SelectItem value="100+">100+ employees</SelectItem>
                    </SelectContent>
                  </Select>
                </div>
                <div className="space-y-2 md:col-span-2">
                  <Label>Address</Label>
                  <Textarea
                    value={company.address}
                    onChange={(e) => setCompany((p) => ({ ...p, address: e.target.value }))}
                    data-testid="address-input"
                  />
                </div>
                <div className="space-y-2">
                  <Label>GSTIN</Label>
                  <Input value={company.gstin} onChange={(e) => setCompany((p) => ({ ...p, gstin: e.target.value }))} data-testid="gstin-input" />
                </div>
                <div className="space-y-2">
                  <Label>PAN</Label>
                  <Input value={company.pan} onChange={(e) => setCompany((p) => ({ ...p, pan: e.target.value }))} data-testid="pan-input" />
                </div>
                <div className="space-y-2">
                  <Label>Billing Email</Label>
                  <Input value={company.billingEmail} onChange={(e) => setCompany((p) => ({ ...p, billingEmail: e.target.value }))} data-testid="billing-email-input" />
                </div>
                <div className="space-y-2">
                  <Label>Billing Phone</Label>
                  <Input value={company.billingPhone} onChange={(e) => setCompany((p) => ({ ...p, billingPhone: e.target.value }))} data-testid="billing-phone-input" />
                </div>
              </div>
            </CardContent>
          </Card>
        </TabsContent>

        {/* Notifications Tab */}
        <TabsContent value="notifications">
          <Card className="border-slate-200">
            <CardHeader>
              <CardTitle>Notification Preferences</CardTitle>
              <CardDescription>Choose how you want to be notified</CardDescription>
            </CardHeader>
            <CardContent className="space-y-6">
              <div className="space-y-4">
                <h4 className="font-medium text-slate-900">Desktop & Sound</h4>
                <p className="text-xs text-slate-500">Last saved: {fmt(notifyUpdatedAtUtc)}</p>
                <div className="space-y-4">
                  <div className="flex items-center justify-between py-3 border-b border-slate-100">
                    <div>
                      <p className="font-medium text-slate-900">Desktop notifications</p>
                      <p className="text-sm text-slate-500">Show system notifications when tab is inactive</p>
                    </div>
                    <Switch checked={!!notifyPrefs.desktopEnabled} onCheckedChange={(v) => setNotifyPrefs((p) => ({ ...p, desktopEnabled: !!v }))} />
                  </div>
                  <div className="flex items-center justify-between py-3 border-b border-slate-100">
                    <div>
                      <p className="font-medium text-slate-900">Notification sound</p>
                      <p className="text-sm text-slate-500">Play sound for new messages</p>
                    </div>
                    <Switch checked={!!notifyPrefs.soundEnabled} onCheckedChange={(v) => setNotifyPrefs((p) => ({ ...p, soundEnabled: !!v }))} />
                  </div>
                  <div className="grid md:grid-cols-2 gap-4">
                    <div className="space-y-2">
                      <Label>Sound style</Label>
                      <Select value={notifyPrefs.soundStyle || "whatsapp"} onValueChange={(v) => setNotifyPrefs((p) => ({ ...p, soundStyle: v }))}>
                        <SelectTrigger><SelectValue /></SelectTrigger>
                        <SelectContent>
                          <SelectItem value="whatsapp">WhatsApp</SelectItem>
                          <SelectItem value="classic">Classic</SelectItem>
                          <SelectItem value="soft">Soft</SelectItem>
                          <SelectItem value="double">Double</SelectItem>
                          <SelectItem value="chime">Chime</SelectItem>
                          <SelectItem value="off">Off</SelectItem>
                        </SelectContent>
                      </Select>
                    </div>
                    <div className="space-y-2">
                      <Label>Volume ({Number(notifyPrefs.soundVolume || 1).toFixed(1)})</Label>
                      <Input type="range" min="0" max="2" step="0.1" value={notifyPrefs.soundVolume} onChange={(e) => setNotifyPrefs((p) => ({ ...p, soundVolume: Number(e.target.value) || 1 }))} />
                    </div>
                  </div>
                </div>
              </div>

              <div className="space-y-4">
                <h4 className="font-medium text-slate-900">In-App Alerts</h4>
                <div className="space-y-4">
                  <div className="flex items-center justify-between py-3 border-b border-slate-100">
                    <div>
                      <p className="font-medium text-slate-900">New messages</p>
                      <p className="text-sm text-slate-500">Show in-app inbox alerts</p>
                    </div>
                    <Switch checked={!!notifyPrefs.inAppNewMessages} onCheckedChange={(v) => setNotifyPrefs((p) => ({ ...p, inAppNewMessages: !!v }))} />
                  </div>
                  <div className="flex items-center justify-between py-3 border-b border-slate-100 last:border-0">
                    <div>
                      <p className="font-medium text-slate-900">System alerts</p>
                      <p className="text-sm text-slate-500">Show warning and health alerts</p>
                    </div>
                    <Switch checked={!!notifyPrefs.inAppSystemAlerts} onCheckedChange={(v) => setNotifyPrefs((p) => ({ ...p, inAppSystemAlerts: !!v }))} />
                  </div>
                </div>
              </div>
            </CardContent>
          </Card>
        </TabsContent>        {/* Security Tab */}
        <TabsContent value="security">
          <div className="space-y-6">
            <Card className="border-slate-200">
              <CardHeader>
                <CardTitle>Account Security</CardTitle>
                <CardDescription>Authenticator status, current login context, and recent web session visibility.</CardDescription>
              </CardHeader>
              <CardContent className="space-y-4">
                <div className={`rounded-xl border p-4 text-sm ${authenticatorEnabled ? "border-emerald-200 bg-emerald-50 text-emerald-900" : "border-amber-200 bg-amber-50 text-amber-900"}`}>
                  {authenticatorEnabled
                    ? `Two-factor authentication is enabled using ${authenticatorProvider || "your authenticator app"}. Login and sensitive actions now require a valid 6-digit code.`
                    : "Two-factor authentication is not enabled on this account yet. Open Integrations > Security and scan the QR code in Google Authenticator or Microsoft Authenticator."}
                </div>
                <div className="grid md:grid-cols-2 gap-4 text-sm">
                  <div className="rounded-lg border border-slate-200 p-4">
                    <p className="font-medium text-slate-900">Authenticated user</p>
                    <p className="text-slate-600 mt-1">{displayName}</p>
                    <p className="text-slate-500">{session.email || "No email available"}</p>
                  </div>
                  <div className="rounded-lg border border-slate-200 p-4">
                    <p className="font-medium text-slate-900">Current project</p>
                    <p className="text-slate-600 mt-1">{session.projectName || session.tenantSlug || "Not selected"}</p>
                    <p className="text-slate-500">Role: {session.role || "unknown"}</p>
                  </div>
                </div>
              </CardContent>
            </Card>

            <Card className="border-slate-200">
              <CardHeader>
                <CardTitle>Two-Factor Authentication</CardTitle>
                <CardDescription>Current authenticator enrollment state for this login account.</CardDescription>
              </CardHeader>
              <CardContent className="space-y-4">
                <div className="rounded-2xl border border-slate-200 bg-slate-50 p-4">
                  <div className="flex flex-col gap-4 md:flex-row md:items-center md:justify-between">
                    <div>
                      <p className="font-medium text-slate-900">{authenticatorEnabled ? "Authenticator enabled" : "Authenticator not enabled"}</p>
                      <p className="mt-1 text-sm text-slate-500">
                        {authenticatorEnabled
                          ? `${authenticatorProvider || "Authenticator"} enrolled on ${fmt(securityOverview?.authenticator?.enrolledAtUtc)}`
                          : "No authenticator is enrolled yet for this account."}
                      </p>
                    </div>
                    <div className="flex items-center gap-3">
                      <Badge className={authenticatorEnabled ? "bg-emerald-100 text-emerald-700 hover:bg-emerald-100" : "bg-slate-100 text-slate-600 hover:bg-slate-100"}>
                        {authenticatorEnabled ? "Enabled" : "Disabled"}
                      </Badge>
                      <Button variant="outline" onClick={() => window.location.assign("/dashboard/integrations")}>
                        {authenticatorEnabled ? "Manage 2FA" : "Enable 2FA"}
                      </Button>
                    </div>
                  </div>
                </div>
              </CardContent>
            </Card>

            <Card className="border-slate-200">
              <CardHeader>
                <CardTitle>Active Sessions</CardTitle>
                <CardDescription>Recent web sessions with IP address and device metadata.</CardDescription>
              </CardHeader>
              <CardContent>
                <div className="space-y-4">
                  {(recentSessions.length ? recentSessions : [securityOverview?.currentSession].filter(Boolean)).map((row) => (
                    <div key={row.id || "current"} className="flex items-center justify-between gap-4 rounded-lg bg-slate-50 p-4">
                      <div className="flex items-center gap-4">
                        <div className="w-10 h-10 bg-green-100 rounded-lg flex items-center justify-center">
                          <Globe className="w-5 h-5 text-green-600" />
                        </div>
                        <div>
                          <p className="font-medium text-slate-900">{row.isCurrent ? "Current web session" : (row.deviceLabel || "Recent session")}</p>
                          <p className="text-sm text-slate-500">{row.lastSeenIpAddress || row.createdIpAddress || "IP unavailable"} • {row.deviceLabel || "Unknown device"}</p>
                          <p className="text-xs text-slate-500">Started {fmt(row.createdAtUtc)} • Last seen {fmt(row.lastSeenAtUtc || row.createdAtUtc)}</p>
                          {row.twoFactorVerified ? <p className="text-xs text-emerald-600">2FA verified</p> : null}
                        </div>
                      </div>
                      <Badge className={row.isRevoked ? "bg-slate-100 text-slate-600 hover:bg-slate-100" : "bg-green-100 text-green-700 hover:bg-green-100"}>
                        {row.isRevoked ? "Revoked" : row.isCurrent ? "Active" : "Recent"}
                      </Badge>
                    </div>
                  ))}
                  <div className="rounded-lg border border-slate-200 p-4 text-sm text-slate-600">
                    {securityOverview?.sessionRetention?.note || "Session metadata is captured for recent web sessions. Lat/long is not collected in the current deployment."}
                  </div>
                </div>
              </CardContent>
            </Card>
          </div>
        </TabsContent>

        {/* WhatsApp Tab */}
        <TabsContent value="whatsapp">
          <div className="space-y-4">
            <h2 className="text-3xl font-heading font-bold text-slate-900">Channel Setup</h2>

            <div className="grid lg:grid-cols-2 gap-4">
              <Card className="border-slate-200 bg-white">
                <CardContent className="p-4 flex items-center gap-3">
                  <div className="w-10 h-10 rounded-full bg-green-100 flex items-center justify-center">
                    <MessageSquare className="w-5 h-5 text-green-700" />
                  </div>
                  <div className="flex-1 text-sm text-slate-700">
                    {whatsappConnected ? "Your WhatsApp number is connected" : "Your WhatsApp number is not connected"}
                  </div>
                  <Button
                    onClick={handleWabaConnect}
                    disabled={connectingWaba}
                    className="bg-orange-500 hover:bg-orange-600 text-white"
                  >
                    {connectingWaba ? <Loader2 className="w-4 h-4 mr-2 animate-spin" /> : null}
                    {whatsappConnected ? "Reconnect Number" : "Connect Number"}
                  </Button>
                </CardContent>
              </Card>

              <Card className="border-slate-200 bg-white">
                <CardContent className="p-4 flex items-center gap-3">
                  <div className="w-10 h-10 rounded-xl bg-pink-100 flex items-center justify-center">
                    <Instagram className="w-5 h-5 text-pink-600" />
                  </div>
                  <div className="flex-1 text-sm text-slate-700">Your Business Instagram is not connected</div>
                  <Button variant="outline" disabled>
                    Connect Account
                  </Button>
                </CardContent>
              </Card>
            </div>

            <Card className="border-slate-200">
              <CardContent className="p-4">
                <div className="inline-flex rounded-xl bg-slate-100 p-1 gap-1">
                  <button
                    onClick={() => setWabaView("whatsapp")}
                    className={`px-4 py-2 rounded-lg text-sm font-medium transition ${
                      wabaView === "whatsapp" ? "bg-white text-orange-600 shadow-sm" : "text-slate-600"
                    }`}
                  >
                    WhatsApp
                  </button>
                  <button
                    onClick={() => setWabaView("instagram")}
                    className={`px-4 py-2 rounded-lg text-sm font-medium transition ${
                      wabaView === "instagram" ? "bg-white text-orange-600 shadow-sm" : "text-slate-600"
                    }`}
                  >
                    Instagram
                  </button>
                </div>
              </CardContent>
            </Card>

            {wabaView === "instagram" ? (
              <Card className="border-slate-200">
                <CardContent className="p-8 text-center">
                  <div className="mx-auto w-12 h-12 rounded-xl bg-pink-100 flex items-center justify-center mb-3">
                    <Instagram className="w-6 h-6 text-pink-600" />
                  </div>
                  <h3 className="text-xl font-semibold text-slate-900">Instagram setup is coming soon</h3>
                  <p className="text-slate-600 mt-2">Connect your Instagram business profile from this screen in the next update.</p>
                </CardContent>
              </Card>
            ) : null}

            {wabaView === "whatsapp" && !whatsappConnected ? (
              <Card className="border-slate-200">
                <CardHeader>
                  <CardTitle>Setup FREE WhatsApp Business Account</CardTitle>
                  <CardDescription>Use Embedded Signup to connect your business in a guided flow</CardDescription>
                </CardHeader>
                <CardContent className="space-y-4">
                  <div className="rounded-xl border border-orange-100 bg-orange-50 p-4">
                    <div className="text-sm font-semibold text-orange-800">Apply for WhatsApp Business API</div>
                    <p className="text-sm text-slate-600 mt-2">
                      Click on <b>Continue with Facebook</b> to start Embedded Signup. Requirement: registered business and working website.
                    </p>
                  </div>

                  <div className="grid md:grid-cols-4 gap-3">
                    {[
                      { step: "Step 1", label: "Start onboarding", done: ["requested", "code_received", "exchanged", "assets_linked", "webhook_subscribed", "verified", "ready"].includes(whatsappStatus.state) },
                      { step: "Step 2", label: "Code exchange", done: ["exchanged", "assets_linked", "webhook_subscribed", "verified", "ready"].includes(whatsappStatus.state) },
                      { step: "Step 3", label: "Assets linked", done: ["assets_linked", "webhook_subscribed", "verified", "ready"].includes(whatsappStatus.state) },
                      { step: "Step 4", label: "Ready to send", done: whatsappReady },
                    ].map((s) => (
                      <div key={s.step} className={`rounded-lg border p-3 ${s.done ? "border-green-200 bg-green-50" : "border-slate-200 bg-white"}`}>
                        <div className="text-xs font-semibold text-slate-500">{s.step}</div>
                        <div className="mt-1 flex items-center gap-2 text-sm font-medium text-slate-900">
                          {s.done ? <CheckCircle2 className="w-4 h-4 text-green-600" /> : <AlertCircle className="w-4 h-4 text-slate-400" />}
                          {s.label}
                        </div>
                      </div>
                    ))}
                  </div>

                  <div className="flex flex-wrap gap-2">
                    <Button onClick={handleWabaStart} disabled={startingWaba} variant="outline">
                      {startingWaba ? <Loader2 className="w-4 h-4 mr-2 animate-spin" /> : null}
                      Start Onboarding
                    </Button>
                    <Button onClick={handleWabaConnect} disabled={connectingWaba} className="bg-orange-500 hover:bg-orange-600 text-white">
                      {connectingWaba ? <Loader2 className="w-4 h-4 mr-2 animate-spin" /> : null}
                      Continue with Facebook
                    </Button>
                    <Button onClick={handleWabaRecheck} disabled={checkingWaba} variant="outline">
                      {checkingWaba ? <Loader2 className="w-4 h-4 mr-2 animate-spin" /> : null}
                      Refresh Checks
                    </Button>
                  </div>

                  <div className="rounded-lg border border-slate-200 bg-white p-3 text-sm text-slate-600">
                    Current status: <span className="font-semibold text-slate-900">{whatsappStatus.state || "requested"}</span>
                    {whatsappStatus.lastError ? <div className="text-amber-700 mt-1">Last error: {whatsappStatus.lastError}</div> : null}
                  </div>
                  <div className="grid md:grid-cols-2 gap-3 text-xs">
                    <div className="rounded-lg border border-slate-200 bg-white p-3">
                      <div className="text-slate-500">Business Manager ID</div>
                      <div className="text-slate-900 font-medium break-all">{whatsappStatus.businessManagerId || "Pending"}</div>
                    </div>
                    <div className="rounded-lg border border-slate-200 bg-white p-3">
                      <div className="text-slate-500">System User ID</div>
                      <div className="text-slate-900 font-medium break-all">{whatsappStatus.systemUserId || "Pending"}</div>
                    </div>
                    <div className="rounded-lg border border-slate-200 bg-white p-3">
                      <div className="text-slate-500">Token Source</div>
                      <div className="text-slate-900 font-medium">{whatsappStatus.tokenSource || "exchanged_token"}</div>
                    </div>
                    <div className="rounded-lg border border-slate-200 bg-white p-3">
                      <div className="text-slate-500">Permanent Token Issued</div>
                      <div className="text-slate-900 font-medium">{fmt(whatsappStatus.permanentTokenIssuedAtUtc)}</div>
                    </div>
                  </div>
                </CardContent>
              </Card>
            ) : null}

            {wabaView === "whatsapp" && whatsappConnected ? (
              <div className="grid xl:grid-cols-3 gap-4">
                <Card className="xl:col-span-2 border-slate-200">
                  <CardHeader>
                    <CardTitle>WhatsApp Cloud API Setup</CardTitle>
                    <CardDescription>Connected and operational details</CardDescription>
                  </CardHeader>
                  <CardContent className="space-y-4">
                    <div className="grid md:grid-cols-2 gap-3">
                      <div className="rounded-lg border border-slate-200 p-3">
                        <p className="text-xs text-slate-500">Business Name</p>
                        <p className="font-semibold text-slate-900">{whatsappStatus.businessName || company.companyName || session.projectName || "Not available"}</p>
                      </div>
                      <div className="rounded-lg border border-slate-200 p-3">
                        <p className="text-xs text-slate-500">Display Number</p>
                        <p className="font-semibold text-slate-900">{whatsappStatus.displayPhoneNumber || "Not available"}</p>
                      </div>
                      <div className="rounded-lg border border-slate-200 p-3">
                        <p className="text-xs text-slate-500">Phone Number ID</p>
                        <p className="font-semibold text-slate-900 break-all">{whatsappStatus.phoneNumberId || "Not available"}</p>
                      </div>
                      <div className="rounded-lg border border-slate-200 p-3">
                        <p className="text-xs text-slate-500">WABA ID</p>
                        <p className="font-semibold text-slate-900 break-all">{whatsappStatus.wabaId || "Not available"}</p>
                      </div>
                      <div className="rounded-lg border border-slate-200 p-3">
                        <p className="text-xs text-slate-500">Business Manager ID</p>
                        <p className="font-semibold text-slate-900 break-all">{whatsappStatus.businessManagerId || "Not available"}</p>
                      </div>
                      <div className="rounded-lg border border-slate-200 p-3">
                        <p className="text-xs text-slate-500">System User</p>
                        <p className="font-semibold text-slate-900 break-all">{whatsappStatus.systemUserName || whatsappStatus.systemUserId || "Not available"}</p>
                      </div>
                      <div className="rounded-lg border border-slate-200 p-3">
                        <p className="text-xs text-slate-500">Token Source</p>
                        <p className="font-semibold text-slate-900">{whatsappStatus.tokenSource || "Not available"}</p>
                      </div>
                      <div className="rounded-lg border border-slate-200 p-3">
                        <p className="text-xs text-slate-500">Permanent Token Issued</p>
                        <p className="font-semibold text-slate-900">{fmt(whatsappStatus.permanentTokenIssuedAtUtc)}</p>
                      </div>
                      <div className="rounded-lg border border-slate-200 p-3">
                        <p className="text-xs text-slate-500">Permanent Token Expiry</p>
                        <p className="font-semibold text-slate-900">{fmt(whatsappStatus.permanentTokenExpiresAtUtc)}</p>
                      </div>
                    </div>

                    <div className="rounded-lg border border-slate-200 p-3">
                      <p className="text-xs text-slate-500 mb-2">Connection Health</p>
                      <div className="flex flex-wrap gap-2">
                        <Badge className="bg-green-100 text-green-700 hover:bg-green-100">
                          <CheckCircle2 className="w-3 h-3 mr-1" /> Connected
                        </Badge>
                        <Badge className={whatsappReady ? "bg-green-100 text-green-700 hover:bg-green-100" : "bg-amber-100 text-amber-700 hover:bg-amber-100"}>
                          {whatsappReady ? <CheckCircle2 className="w-3 h-3 mr-1" /> : <AlertCircle className="w-3 h-3 mr-1" />}
                          {whatsappReady ? "Ready to Send" : "Checks Pending"}
                        </Badge>
                        <Badge variant="outline" className="border-slate-300 text-slate-700">
                          State: {whatsappStatus.state || "connected"}
                        </Badge>
                      </div>
                      {whatsappStatus.lastError ? (
                        <p className="text-xs text-amber-700 mt-2">{whatsappStatus.lastError}</p>
                      ) : null}
                    </div>

                    <div className="rounded-lg border border-slate-200 p-3">
                      <p className="text-xs text-slate-500 mb-2">Lifecycle Timeline</p>
                      <div className="grid md:grid-cols-2 gap-2 text-xs">
                        <div className="rounded border border-slate-100 bg-slate-50 p-2"><span className="text-slate-500">Requested:</span> <span className="text-slate-900">{fmt(whatsappStatus.timeline?.requestedAtUtc)}</span></div>
                        <div className="rounded border border-slate-100 bg-slate-50 p-2"><span className="text-slate-500">Code received:</span> <span className="text-slate-900">{fmt(whatsappStatus.timeline?.codeReceivedAtUtc)}</span></div>
                        <div className="rounded border border-slate-100 bg-slate-50 p-2"><span className="text-slate-500">Exchanged:</span> <span className="text-slate-900">{fmt(whatsappStatus.timeline?.exchangedAtUtc)}</span></div>
                        <div className="rounded border border-slate-100 bg-slate-50 p-2"><span className="text-slate-500">Assets linked:</span> <span className="text-slate-900">{fmt(whatsappStatus.timeline?.assetsLinkedAtUtc)}</span></div>
                        <div className="rounded border border-slate-100 bg-slate-50 p-2"><span className="text-slate-500">Webhook subscribed:</span> <span className="text-slate-900">{fmt(whatsappStatus.timeline?.webhookSubscribedAtUtc)}</span></div>
                        <div className="rounded border border-slate-100 bg-slate-50 p-2"><span className="text-slate-500">Verified:</span> <span className="text-slate-900">{fmt(whatsappStatus.timeline?.verifiedAtUtc)}</span></div>
                      </div>
                    </div>

                    <div className="flex flex-wrap gap-2">
                      <Button onClick={handleWabaRecheck} disabled={checkingWaba} variant="outline">
                        {checkingWaba ? <Loader2 className="w-4 h-4 mr-2 animate-spin" /> : null}
                        Refresh Checks
                      </Button>
                      <Button onClick={handleWabaConnect} disabled={connectingWaba} className="bg-orange-500 hover:bg-orange-600 text-white">
                        {connectingWaba ? <Loader2 className="w-4 h-4 mr-2 animate-spin" /> : null}
                        Reconnect WhatsApp
                      </Button>
                    </div>
                  </CardContent>
                </Card>

                <Card className="border-slate-200">
                  <CardHeader>
                    <CardTitle>Quick Actions</CardTitle>
                  </CardHeader>
                  <CardContent className="space-y-2">
                    <Button variant="outline" className="w-full justify-between" onClick={handleWabaStart} disabled={startingWaba}>
                      Start/Re-run Onboarding
                      {startingWaba ? <Loader2 className="w-4 h-4 animate-spin" /> : <ChevronRight className="w-4 h-4" />}
                    </Button>
                    <Button variant="outline" className="w-full justify-between">
                      Open Meta Docs <ExternalLink className="w-4 h-4" />
                    </Button>
                    <Button variant="outline" className="w-full justify-between">
                      API & Webhooks <ChevronRight className="w-4 h-4" />
                    </Button>
                  </CardContent>
                </Card>
              </div>
            ) : null}
          </div>
        </TabsContent>
      </Tabs>
    </div>
  );
};

export default SettingsPage;

