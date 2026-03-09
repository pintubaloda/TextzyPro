import { useEffect, useMemo, useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import ApiDocsViewer from "@/components/docs/ApiDocsViewer";
import {
  BookOpenText,
  CreditCard,
  ExternalLink,
  FileText,
  LockKeyhole,
  Plug,
  QrCode,
  Shield,
  ShieldCheck,
} from "lucide-react";
import { toast } from "sonner";
import {
  createIntegrationRazorpayOrder,
  disableAuthenticator,
  getAuthenticatorStatus,
  getBillingPaymentConfig,
  getIntegrationCatalog,
  getSession,
  setupAuthenticator,
  verifyRazorpayPayment,
  verifyAuthenticator,
} from "@/lib/api";

const CATEGORY_META = {
  all: { title: "All Integrations", hint: "Everything available to this tenant right now." },
  security: { title: "Security", hint: "Protect account access with authenticator-based 2FA." },
  payments: { title: "Payments", hint: "Billing, invoicing, and payment event connections." },
  "e-commerce": { title: "E-commerce", hint: "Order and cart workflows from storefront platforms." },
  automation: { title: "Automation", hint: "External workflow bridges and orchestration tools." },
  crm: { title: "CRM", hint: "Customer sync and pipeline integrations." },
  marketing: { title: "Marketing", hint: "Audience sync and campaign distribution tools." },
  general: { title: "General", hint: "Platform utilities and shared services." },
};

const PROVIDER_LABEL = {
  google_authenticator: "Google Authenticator",
  microsoft_authenticator: "Microsoft Authenticator",
};

function priceLabel(item) {
  if (String(item.pricingType || "free").toLowerCase() !== "paid") return "Free";
  const amount = `₹${Number(item.price || 0).toLocaleString()}`;
  const frequency = String(item.billingFrequency || "monthly").toLowerCase() === "one_time" ? "one-time" : "/month";
  const tax = String(item.taxMode || "exclusive").toLowerCase() === "inclusive" ? " incl. GST" : " + GST";
  return `${amount} ${frequency}${tax}`;
}

function categoryBadge(category) {
  return CATEGORY_META[category]?.title || category;
}

function resolveStatusMeta(item) {
  const status = String(item?.activationStatus || "").toLowerCase();
  if (status === "active") {
    return {
      label: "Purchased",
      className: "border-emerald-200 bg-emerald-50 text-emerald-700",
      hint: "Enabled for this tenant"
    };
  }
  if (status === "not_purchased") {
    return {
      label: "Not Purchased",
      className: "border-amber-200 bg-amber-50 text-amber-700",
      hint: "Direct checkout available for this add-on"
    };
  }
  if (status === "available") {
    return {
      label: "Available",
      className: "border-amber-200 bg-amber-50 text-amber-700",
      hint: "Not purchased or assigned yet"
    };
  }
  return {
    label: "Unavailable",
    className: "border-slate-200 bg-slate-100 text-slate-600",
    hint: "Disabled by platform owner"
  };
}

const IntegrationsPage = () => {
  const session = getSession();
  const isSuperAdmin = String(session?.role || "").toLowerCase() === "super_admin";
  const [catalog, setCatalog] = useState([]);
  const [catalogBusy, setCatalogBusy] = useState(true);
  const [paymentConfig, setPaymentConfig] = useState(null);
  const [checkoutBusySlug, setCheckoutBusySlug] = useState("");
  const [authenticator, setAuthenticator] = useState({ enabled: false, provider: "", enrolledAtUtc: "" });
  const [setupState, setSetupState] = useState({ provider: "", qrUrl: "", code: "", busy: false });
  const [category, setCategory] = useState("all");
  const [docViewer, setDocViewer] = useState({ open: false, type: "sms" });

  const categories = useMemo(() => {
    const values = new Set(["all"]);
    for (const item of catalog) values.add(String(item.category || "general").toLowerCase());
    return Array.from(values);
  }, [catalog]);

  const visibleItems = useMemo(() => {
    const rows = (catalog || []).filter((item) => item?.isVisible !== false);
    if (category === "all") return rows;
    return rows.filter((item) => String(item.category || "general").toLowerCase() === category);
  }, [catalog, category]);

  const kpis = useMemo(() => {
    const rows = catalog || [];
    return {
      active: rows.filter((x) => x?.isActive !== false).length,
      paid: rows.filter((x) => String(x?.pricingType || "").toLowerCase() === "paid").length,
      security: rows.filter((x) => String(x?.category || "").toLowerCase() === "security").length,
      enabledSecurity: authenticator?.enabled ? 1 : 0,
    };
  }, [catalog, authenticator]);

  useEffect(() => {
    let alive = true;
    (async () => {
      try {
        setCatalogBusy(true);
        const [catalogRows, paymentCfg, authStatus] = await Promise.all([
          getIntegrationCatalog().catch(() => []),
          getBillingPaymentConfig().catch(() => null),
          getAuthenticatorStatus().catch(() => ({ enabled: false, provider: "", enrolledAtUtc: "" })),
        ]);
        if (!alive) return;
        setCatalog(Array.isArray(catalogRows) ? catalogRows : []);
        setPaymentConfig(paymentCfg || null);
        setAuthenticator(authStatus || { enabled: false, provider: "", enrolledAtUtc: "" });
      } catch (e) {
        if (!alive) return;
        toast.error(e?.message || "Failed to load integrations");
      } finally {
        if (alive) setCatalogBusy(false);
      }
    })();
    return () => {
      alive = false;
    };
  }, []);

  const ensureRazorpayScript = async () => {
    if (window.Razorpay) return true;
    await new Promise((resolve, reject) => {
      const existing = document.querySelector('script[data-razorpay-sdk="1"]');
      if (existing) {
        existing.addEventListener("load", () => resolve(true), { once: true });
        existing.addEventListener("error", () => reject(new Error("Failed to load Razorpay SDK")), { once: true });
        return;
      }
      const script = document.createElement("script");
      script.src = "https://checkout.razorpay.com/v1/checkout.js";
      script.async = true;
      script.dataset.razorpaySdk = "1";
      script.onload = () => resolve(true);
      script.onerror = () => reject(new Error("Failed to load Razorpay SDK"));
      document.body.appendChild(script);
    });
    return !!window.Razorpay;
  };

  const reloadCatalog = async () => {
    const rows = await getIntegrationCatalog().catch(() => []);
    setCatalog(Array.isArray(rows) ? rows : []);
  };

  const handlePurchaseIntegration = async (item) => {
    try {
      setCheckoutBusySlug(item.slug);
      const cfg = paymentConfig;
      const checkoutKey = cfg?.razorpay?.checkoutKeyId || cfg?.razorpay?.keyId || "";
      if (!cfg?.razorpay?.enabled || !checkoutKey) throw new Error("Razorpay is not configured.");

      const order = await createIntegrationRazorpayOrder(item.slug);
      await ensureRazorpayScript();

      await new Promise((resolve, reject) => {
        const rzp = new window.Razorpay({
          key: order.keyId || checkoutKey,
          amount: order.amount,
          currency: order.currency || "INR",
          order_id: order.orderId,
          name: "Textzy",
          description: `${item.name} purchase`,
          handler: async function (resp) {
            try {
              await verifyRazorpayPayment({
                razorpayOrderId: resp.razorpay_order_id,
                razorpayPaymentId: resp.razorpay_payment_id,
                razorpaySignature: resp.razorpay_signature,
              });
              resolve(true);
            } catch (error) {
              reject(error);
            }
          },
          modal: {
            ondismiss: () => reject(new Error("Payment cancelled.")),
          },
          theme: { color: "#f97316" },
        });
        rzp.open();
      });

      toast.success(`${item.name} purchased successfully.`);
      await reloadCatalog();
    } catch (error) {
      toast.error(error?.message || "Integration purchase failed");
    } finally {
      setCheckoutBusySlug("");
    }
  };

  const beginAuthenticatorSetup = async (provider) => {
    try {
      setSetupState((prev) => ({ ...prev, busy: true, provider, code: "" }));
      const res = await setupAuthenticator(provider);
      setSetupState({ provider, qrUrl: res?.qrUrl || "", code: "", busy: false });
      toast.success(`${PROVIDER_LABEL[res?.provider || provider] || "Authenticator"} QR generated`);
    } catch (e) {
      setSetupState({ provider: "", qrUrl: "", code: "", busy: false });
      toast.error(e?.message || "Failed to initialize authenticator");
    }
  };

  const submitAuthenticatorCode = async () => {
    try {
      setSetupState((prev) => ({ ...prev, busy: true }));
      const res = await verifyAuthenticator(setupState.code);
      setAuthenticator(res || { enabled: true, provider: setupState.provider });
      setSetupState({ provider: "", qrUrl: "", code: "", busy: false });
      toast.success("Authenticator enabled");
    } catch (e) {
      setSetupState((prev) => ({ ...prev, busy: false }));
      toast.error(e?.message || "Invalid authenticator code");
    }
  };

  const removeAuthenticator = async () => {
    try {
      await disableAuthenticator();
      setAuthenticator({ enabled: false, provider: "", enrolledAtUtc: "" });
      setSetupState({ provider: "", qrUrl: "", code: "", busy: false });
      toast.success("Authenticator disabled");
    } catch (e) {
      toast.error(e?.message || "Failed to disable authenticator");
    }
  };

  return (
    <div className="space-y-6" data-testid="integrations-page">
      <div className="flex flex-col gap-2 md:flex-row md:items-end md:justify-between">
        <div>
          <h1 className="text-2xl font-heading font-bold text-slate-900">Integrations</h1>
          <p className="text-slate-600">Platform-managed add-ons, security connectors, and public API access for project <strong>{session?.tenantSlug || "n/a"}</strong>.</p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Badge variant="outline" className="rounded-full border-slate-200 px-3 py-1 text-slate-700">
            Tenant slug: {session?.tenantSlug || "n/a"}
          </Badge>
          <Button variant="outline" onClick={() => setDocViewer({ open: true, type: "sms" })}>
            <BookOpenText className="mr-2 h-4 w-4" />
            View API Docs
          </Button>
        </div>
      </div>

      <div className="grid gap-4 xl:grid-cols-4 md:grid-cols-2">
        {[
          { title: "Active Integrations", value: kpis.active, hint: "Currently visible and enabled", icon: Plug, tone: "bg-orange-50 text-orange-600" },
          { title: "Paid Add-ons", value: kpis.paid, hint: "Commercial integrations with pricing", icon: CreditCard, tone: "bg-blue-50 text-blue-600" },
          { title: "Security Options", value: kpis.security, hint: "Security category integrations", icon: ShieldCheck, tone: "bg-emerald-50 text-emerald-600" },
          { title: "2FA Enabled", value: kpis.enabledSecurity, hint: authenticator?.enabled ? `Using ${PROVIDER_LABEL[authenticator.provider] || authenticator.provider}` : "No authenticator enrolled", icon: LockKeyhole, tone: "bg-violet-50 text-violet-600" },
        ].map((item) => (
          <Card key={item.title} className="border-slate-200 shadow-sm">
            <CardContent className="flex items-start justify-between p-5">
              <div>
                <p className="text-xs uppercase tracking-[0.16em] text-slate-500">{item.title}</p>
                <p className="mt-2 text-3xl font-bold text-slate-950">{item.value}</p>
                <p className="mt-1 text-sm text-slate-500">{item.hint}</p>
              </div>
              <div className={`flex h-11 w-11 items-center justify-center rounded-2xl ${item.tone}`}>
                <item.icon className="h-5 w-5" />
              </div>
            </CardContent>
          </Card>
        ))}
      </div>

      <div className="grid gap-6 2xl:grid-cols-[1.4fr_1fr]">
        <Card className="border-slate-200 shadow-sm">
          <CardHeader>
            <CardTitle>Integration Catalog</CardTitle>
            <CardDescription>{CATEGORY_META[category]?.hint || "Platform-managed integrations available to this tenant."}</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <Tabs value={category} onValueChange={setCategory}>
              <TabsList className="flex h-auto w-full flex-wrap justify-start gap-2 bg-transparent p-0">
                {categories.map((item) => (
                  <TabsTrigger key={item} value={item} className="rounded-full border border-slate-200 bg-white px-4 py-2 data-[state=active]:border-orange-200 data-[state=active]:bg-orange-50 data-[state=active]:text-orange-600">
                    {categoryBadge(item)}
                  </TabsTrigger>
                ))}
              </TabsList>
              <TabsContent value={category} className="mt-4">
                <div className="grid gap-4 xl:grid-cols-2">
                  {visibleItems.map((item) => {
                    const slug = String(item.slug || "");
                    const isSecurity = String(item.category || "").toLowerCase() === "security";
                    const statusMeta = resolveStatusMeta(item);
                    const isCurrentProvider = authenticator?.enabled && (
                      (slug === "google-authenticator" && authenticator.provider === "google_authenticator") ||
                      (slug === "microsoft-authenticator" && authenticator.provider === "microsoft_authenticator")
                    );
                    return (
                      <Card key={slug} className="border-slate-200 shadow-sm">
                        <CardContent className="p-5 space-y-4">
                          <div className="flex items-start justify-between gap-3">
                            <div className="flex items-start gap-3">
                              <div className={`flex h-11 w-11 items-center justify-center rounded-2xl ${isSecurity ? "bg-emerald-50 text-emerald-600" : "bg-slate-100 text-slate-700"}`}>
                                {isSecurity ? <Shield className="h-5 w-5" /> : <Plug className="h-5 w-5" />}
                              </div>
                              <div>
                                <p className="font-semibold text-slate-950">{item.name}</p>
                                <p className="mt-1 text-sm text-slate-500">{item.description}</p>
                              </div>
                            </div>
                            <Badge variant="outline" className={statusMeta.className}>
                              {statusMeta.label}
                            </Badge>
                          </div>
                          <div className="flex flex-wrap gap-2 text-xs">
                            <Badge className="bg-slate-100 text-slate-700 hover:bg-slate-100">{categoryBadge(item.category)}</Badge>
                            <Badge className={String(item.pricingType || "free") === "paid" ? "bg-orange-50 text-orange-700 hover:bg-orange-50" : "bg-emerald-50 text-emerald-700 hover:bg-emerald-50"}>
                              {priceLabel(item)}
                            </Badge>
                          </div>
                          <p className="text-xs text-slate-500">{statusMeta.hint}</p>

                          {isSecurity ? (
                            <div className="rounded-2xl border border-slate-200 bg-slate-50 p-4">
                              {isCurrentProvider ? (
                                <div className="space-y-3">
                                  <div className="flex items-center justify-between">
                                    <div>
                                      <p className="font-medium text-slate-900">Authenticator enabled</p>
                                      <p className="text-sm text-slate-500">{PROVIDER_LABEL[authenticator.provider] || authenticator.provider}</p>
                                    </div>
                                    <Badge className="bg-emerald-50 text-emerald-700 hover:bg-emerald-50">Enabled</Badge>
                                  </div>
                                  <Button variant="outline" className="w-full" onClick={removeAuthenticator}>Disable Authenticator</Button>
                                </div>
                              ) : setupState.provider === (slug === "google-authenticator" ? "google_authenticator" : "microsoft_authenticator") ? (
                                <div className="space-y-4">
                                  <div className="overflow-hidden rounded-2xl border border-slate-200 bg-white p-4">
                                    {setupState.qrUrl ? (
                                      <img src={setupState.qrUrl} alt="Authenticator QR" className="mx-auto h-56 w-56 object-contain" />
                                    ) : (
                                      <div className="flex h-56 items-center justify-center"><QrCode className="h-10 w-10 text-slate-400" /></div>
                                    )}
                                  </div>
                                  <div className="space-y-2">
                                    <Label>Verify 6-digit code</Label>
                                    <Input
                                      inputMode="numeric"
                                      maxLength={6}
                                      placeholder="Enter authenticator code"
                                      value={setupState.code}
                                      onChange={(e) => setSetupState((prev) => ({ ...prev, code: e.target.value }))}
                                    />
                                  </div>
                                  <div className="flex gap-2">
                                    <Button className="bg-orange-500 hover:bg-orange-600" disabled={setupState.busy} onClick={submitAuthenticatorCode}>Verify & Enable</Button>
                                    <Button variant="outline" disabled={setupState.busy} onClick={() => setSetupState({ provider: "", qrUrl: "", code: "", busy: false })}>Cancel</Button>
                                  </div>
                                </div>
                              ) : String(item.activationStatus || "") === "not_purchased" ? (
                                <div className="space-y-3">
                                  <p className="text-sm text-slate-600">
                                    Pay securely with Razorpay to activate {item.name} for this tenant. After purchase, you can scan the QR and complete setup here.
                                  </p>
                                  <Button
                                    className="bg-orange-500 hover:bg-orange-600"
                                    disabled={checkoutBusySlug === item.slug}
                                    onClick={() => handlePurchaseIntegration(item)}
                                  >
                                    {checkoutBusySlug === item.slug ? "Processing..." : `Buy ${item.name}`}
                                  </Button>
                                </div>
                              ) : (
                                <div className="space-y-3">
                                  <p className="text-sm text-slate-600">
                                    {item.isActive
                                      ? `Scan a QR in ${item.name} and verify the 6-digit code. Manual secret entry is not required.`
                                      : "This security add-on is not assigned to this tenant yet. Contact platform owner to enable it."}
                                  </p>
                                  <Button className="bg-orange-500 hover:bg-orange-600" disabled={setupState.busy || item.isActive === false} onClick={() => beginAuthenticatorSetup(slug === "google-authenticator" ? "google_authenticator" : "microsoft_authenticator")}>
                                    {setupState.busy ? "Preparing..." : `Set up ${item.name}`}
                                  </Button>
                                </div>
                              )}
                            </div>
                          ) : (
                            <div className="flex items-center justify-between rounded-2xl border border-slate-200 bg-slate-50 p-4">
                              <div>
                                <p className="font-medium text-slate-900">
                                  {item.isActive
                                    ? (String(item.pricingType || "free") === "paid" ? "Purchased add-on" : "Enabled for this tenant")
                                    : (String(item.activationStatus || "") === "not_purchased"
                                      ? "Buy this add-on"
                                      : String(item.pricingType || "free") === "paid"
                                        ? "Commercial add-on"
                                        : "Available only after assignment")}
                                </p>
                                <p className="text-sm text-slate-500">
                                  {item.isActive
                                    ? "This integration is currently assigned and available for this tenant."
                                    : String(item.activationStatus || "") === "not_purchased"
                                      ? `Pay securely with Razorpay to activate ${item.name} for this tenant immediately.`
                                      : String(item.pricingType || "free") === "paid"
                                      ? `Commercialized by platform owner as ${String(item.billingFrequency || "monthly").replace("_", " ")} add-on.`
                                      : "Platform owner must assign this integration before tenant-side use."}
                                </p>
                              </div>
                              {String(item.activationStatus || "") === "not_purchased" ? (
                                <Button
                                  className="bg-orange-500 text-white hover:bg-orange-600"
                                  disabled={checkoutBusySlug === item.slug}
                                  onClick={() => handlePurchaseIntegration(item)}
                                >
                                  {checkoutBusySlug === item.slug ? "Processing..." : "Buy Now"}
                                </Button>
                              ) : (
                                <ExternalLink className="h-5 w-5 text-slate-400" />
                              )}
                            </div>
                          )}
                        </CardContent>
                      </Card>
                    );
                  })}
                </div>
                {!catalogBusy && visibleItems.length === 0 ? (
                  <div className="rounded-2xl border border-dashed border-slate-300 p-10 text-center text-slate-500">No integrations available in this category.</div>
                ) : null}
              </TabsContent>
            </Tabs>
          </CardContent>
        </Card>

        <div className="space-y-6">
          <Card className="border-slate-200 shadow-sm">
            <CardHeader>
              <CardTitle>Security Summary</CardTitle>
              <CardDescription>2FA enrollment and enforcement posture for this user session.</CardDescription>
            </CardHeader>
            <CardContent className="space-y-4">
              <div className="rounded-2xl border border-slate-200 bg-slate-50 p-4">
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <p className="font-medium text-slate-950">Authenticator status</p>
                    <p className="mt-1 text-sm text-slate-500">
                      {authenticator?.enabled
                        ? `${PROVIDER_LABEL[authenticator.provider] || authenticator.provider} is active for this account.`
                        : "No authenticator is currently enabled."}
                    </p>
                  </div>
                  {authenticator?.enabled ? <ShieldCheck className="h-5 w-5 text-emerald-600" /> : <Shield className="h-5 w-5 text-slate-400" />}
                </div>
              </div>
              <div className="grid gap-3 sm:grid-cols-2">
                <div className="rounded-2xl border border-slate-200 p-4">
                  <p className="text-xs uppercase tracking-[0.16em] text-slate-500">QR based onboarding</p>
                  <p className="mt-2 font-semibold text-slate-950">Enabled</p>
                  <p className="mt-1 text-sm text-slate-500">No manual secret display in the default flow.</p>
                </div>
                <div className="rounded-2xl border border-slate-200 p-4">
                  <p className="text-xs uppercase tracking-[0.16em] text-slate-500">Provider support</p>
                  <p className="mt-2 font-semibold text-slate-950">Google + Microsoft</p>
                  <p className="mt-1 text-sm text-slate-500">Both apps can scan the generated TOTP QR.</p>
                </div>
              </div>
            </CardContent>
          </Card>

          <Card className="border-slate-200 shadow-sm">
            <CardHeader>
              <CardTitle>Documentation Center</CardTitle>
              <CardDescription>Professional API references for SMS and WhatsApp integrations, available directly inside the platform.</CardDescription>
            </CardHeader>
            <CardContent className="space-y-4">
              <div className="grid gap-3">
                <div className="rounded-2xl border border-slate-200 bg-slate-50 p-4">
                  <div className="flex items-start justify-between gap-3">
                    <div>
                      <p className="font-medium text-slate-950">SMS API Reference</p>
                      <p className="mt-1 text-sm text-slate-500">Simple URL mode, DLT mapping, Tata flow, sender registry, template registry, webhook model, and production checklist.</p>
                    </div>
                    <FileText className="h-5 w-5 text-orange-500" />
                  </div>
                  <div className="mt-4 flex flex-wrap gap-2">
                    <Button className="bg-orange-500 hover:bg-orange-600" onClick={() => setDocViewer({ open: true, type: "sms" })}>Read in App</Button>
                    <Button variant="outline" onClick={() => window.open("/docs/sms-api-reference.html", "_blank", "noopener,noreferrer")}>Open Full Page</Button>
                  </div>
                </div>

                <div className="rounded-2xl border border-slate-200 bg-slate-50 p-4">
                  <div className="flex items-start justify-between gap-3">
                    <div>
                      <p className="font-medium text-slate-950">WhatsApp API Reference</p>
                      <p className="mt-1 text-sm text-slate-500">Messaging, templates, media, webhook, flow builder, automation, smoke testing, diagnostics, and production operations.</p>
                    </div>
                    <FileText className="h-5 w-5 text-sky-500" />
                  </div>
                  <div className="mt-4 flex flex-wrap gap-2">
                    <Button className="bg-orange-500 hover:bg-orange-600" onClick={() => setDocViewer({ open: true, type: "whatsapp" })}>Read in App</Button>
                    <Button variant="outline" onClick={() => window.open("/docs/whatsapp-api-reference.html", "_blank", "noopener,noreferrer")}>Open Full Page</Button>
                  </div>
                </div>

                <div className="rounded-2xl border border-dashed border-slate-300 bg-white p-4 text-sm text-slate-600">
                  <div className="flex items-center justify-between gap-3">
                    <div>
                      <p className="font-medium text-slate-900">Documentation Index</p>
                      <p className="mt-1 text-slate-500">Use the documentation landing page for quick access to HTML references, markdown source, and Postman import files.</p>
                    </div>
                    <Button variant="outline" onClick={() => window.open("/docs/index.html", "_blank", "noopener,noreferrer")}>
                      <ExternalLink className="mr-2 h-4 w-4" />
                      Open Index
                    </Button>
                  </div>
                </div>
              </div>
            </CardContent>
          </Card>

          <Card className="border-slate-200 shadow-sm">
            <CardHeader>
              <CardTitle>Public API Access</CardTitle>
              <CardDescription>Tenant-scoped API credentials are now managed from platform admin, not from this tenant screen.</CardDescription>
            </CardHeader>
            <CardContent className="space-y-4">
              <div className="rounded-2xl border border-amber-200 bg-amber-50 p-4 text-sm text-amber-800">
                {isSuperAdmin ? (
                  <>
                    Tenant-scoped API credentials are managed from <strong>Platform Admin</strong> for each company. This prevents one credential set from being reused across tenants.
                  </>
                ) : (
                  <>
                    Public API credentials are controlled by platform owner. Use project slug <strong>{session?.tenantSlug || "n/a"}</strong> and request the tenant-specific credentials from platform admin.
                  </>
                )}
              </div>
              {isSuperAdmin ? (
                <Button className="w-full bg-orange-500 hover:bg-orange-600" onClick={() => window.location.assign("/dashboard/admin")}>
                  Open Platform Admin
                </Button>
              ) : null}
            </CardContent>
          </Card>
        </div>
      </div>

      <ApiDocsViewer
        open={docViewer.open}
        onOpenChange={(open) => setDocViewer((prev) => ({ ...prev, open }))}
        type={docViewer.type}
        onTypeChange={(nextType) => setDocViewer({ open: true, type: nextType })}
      />
    </div>
  );
};

export default IntegrationsPage;
