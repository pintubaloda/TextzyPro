import { useEffect, useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Progress } from "@/components/ui/progress";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle, DialogTrigger } from "@/components/ui/dialog";
import SupportContactCard from "@/components/support/SupportContactCard";
import {
  CreditCard,
  Download,
  Check,
  Zap,
  MessageSquare,
  Users,
  AlertTriangle,
  Workflow,
  Bot,
  Activity,
} from "lucide-react";
import {
  changeBillingPlan,
  cancelBillingSubscription,
  createRazorpayOrder,
  downloadAllBillingInvoices,
  downloadBillingInvoice,
  getBillingInvoices,
  getBillingDunningStatus,
  getBillingPaymentConfig,
  getBillingPlans,
  getBillingUsage,
  getSession,
  getCompanySettings,
  getCurrentBillingPlan,
  getSupportContext,
  verifyRazorpayPayment
} from "@/lib/api";
import { toast } from "sonner";

const BillingPage = () => {
  const navigate = useNavigate();
  const [showUpgradeDialog, setShowUpgradeDialog] = useState(false);
  const [plans, setPlans] = useState([]);
  const [sub, setSub] = useState(null);
  const [usageValues, setUsageValues] = useState({});
  const [creditBalances, setCreditBalances] = useState({});
  const [invoices, setInvoices] = useState([]);
  const [dunningStatus, setDunningStatus] = useState(null);
  const [paymentConfig, setPaymentConfig] = useState(null);
  const [company, setCompany] = useState(null);
  const [supportContext, setSupportContext] = useState(null);
  const [payingCode, setPayingCode] = useState("");
  const session = useMemo(() => getSession() || {}, []);
  const isSuperAdmin = String(session?.role || "").toLowerCase() === "super_admin";
  const ownerMode = useMemo(() => {
    if (!isSuperAdmin) return "self";
    try {
      return localStorage.getItem("textzy_owner_mode") || "self";
    } catch {
      return "self";
    }
  }, [isSuperAdmin]);

  useEffect(() => {
    (async () => {
      try {
        const [p, c, u, i, d] = await Promise.all([
          getBillingPlans(),
          getCurrentBillingPlan(),
          getBillingUsage(),
          getBillingInvoices(),
          getBillingDunningStatus().catch(() => null)
        ]);
        const companyCfg = await getCompanySettings().catch(() => null);
        const paymentCfg = await getBillingPaymentConfig().catch(() => null);
        const supportCfg = await getSupportContext().catch(() => null);
        setPlans(Array.isArray(p) ? p : []);
        setSub(c || null);
        setUsageValues(u?.values || {});
        setCreditBalances(c?.creditBalances || u?.creditBalances || {});
        setInvoices(Array.isArray(i) ? i : []);
        setDunningStatus(d || null);
        setCompany(companyCfg || null);
        setPaymentConfig(paymentCfg);
        setSupportContext(supportCfg || null);
      } catch (e) {
        toast.error(e.message || "Failed to load billing");
      }
    })();
  }, []);

  const currentPlan = sub?.plan || null;
  const taxRatePercent = Number(company?.taxRatePercent ?? 18);
  const planCost = Number(currentPlan?.priceMonthly || 0);
  const taxMode = String(currentPlan?.taxMode || "exclusive").toLowerCase();
  const taxAmount = company?.isTaxExempt
    ? 0
    : taxMode === "inclusive"
    ? Math.round((planCost - (planCost / (1 + (taxRatePercent / 100)))) * 100) / 100
    : Math.round(planCost * (taxRatePercent / 100));
  const totalAmount = taxMode === "inclusive" ? planCost : planCost + taxAmount;
  const companyName =
    String(company?.companyName || "").trim() ||
    String(session?.projectName || "").trim() ||
    String(session?.tenantSlug || "").trim() ||
    "Your company";
  const legalName = String(company?.legalName || "").trim();
  const billingAddressText = String(company?.address || "").trim();
  const billingEmail =
    String(company?.billingEmail || "").trim() ||
    String(session?.email || "").trim();
  const billingPhone = String(company?.billingPhone || "").trim();
  const gstin = String(company?.gstin || "").trim();
  const pan = String(company?.pan || "").trim();
  const pct = (used, limit) => !limit ? 0 : Math.min(100, Math.round((used / limit) * 100));
  const formatMoneyValue = (value, currency = "INR") =>
    `${String(currency || "INR").toUpperCase() === "INR" ? "\u20B9" : `${String(currency || "INR").toUpperCase()} `}${Number(value || 0).toLocaleString("en-IN", {
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    })}`;
  const describePlanPrice = (plan, cycle = "monthly") => {
    if (!plan) return "-";
    const currency = String(plan.currency || "INR").toUpperCase();
    const taxSuffix = String(plan.taxMode || "exclusive").toLowerCase() === "inclusive" ? " incl. GST" : " + GST";
    if (String(plan.pricingModel || "").toLowerCase() === "usage_pack") {
      return `${formatMoneyValue(plan.priceMonthly || 0, currency)} for ${Number(plan.includedQuantity || 0).toLocaleString()} ${plan.usageUnitName || "units"}${taxSuffix}`;
    }
    const base = cycle === "yearly"
      ? `${formatMoneyValue(plan.priceYearly || 0, currency)}/year`
      : `${formatMoneyValue(plan.priceMonthly || 0, currency)}/month`;
    return `${base}${taxSuffix}`;
  };
  const usage = useMemo(() => {
    const limits = currentPlan?.limits || {};
    return {
      whatsapp: { used: usageValues.whatsappMessages || 0, limit: limits.whatsappMessages || 0, percentage: pct(usageValues.whatsappMessages || 0, limits.whatsappMessages || 0) },
      sms: { used: usageValues.smsCredits || 0, limit: limits.smsCredits || 0, percentage: pct(usageValues.smsCredits || 0, limits.smsCredits || 0), balance: Number(creditBalances.smsCredits || 0) },
      contacts: { used: usageValues.contacts || 0, limit: limits.contacts || 0, percentage: pct(usageValues.contacts || 0, limits.contacts || 0) },
      team: { used: usageValues.teamMembers || 0, limit: limits.teamMembers || 0, percentage: pct(usageValues.teamMembers || 0, limits.teamMembers || 0) },
      flows: { used: usageValues.flows || 0, limit: limits.flows || 0, percentage: pct(usageValues.flows || 0, limits.flows || 0) },
      chatbots: { used: usageValues.chatbots || 0, limit: limits.chatbots || 0, percentage: pct(usageValues.chatbots || 0, limits.chatbots || 0) },
      apiCalls: { used: usageValues.apiCalls || 0, limit: limits.apiCalls || 0, percentage: pct(usageValues.apiCalls || 0, limits.apiCalls || 0) }
    };
  }, [currentPlan, creditBalances, usageValues]);
  const invoiceStats = useMemo(() => {
    const totalInvoices = invoices.length;
    const paidInvoices = invoices.filter((invoice) => String(invoice.status || "").toLowerCase() === "paid").length;
    const totalBilled = invoices.reduce((sum, invoice) => sum + Number(invoice.total || 0), 0);
    return { totalInvoices, paidInvoices, totalBilled };
  }, [invoices]);

  const dunningBadgeClass = useMemo(() => {
    const status = String(dunningStatus?.subscription?.status || "").toLowerCase();
    if (status === "active") return "bg-green-100 text-green-700 hover:bg-green-100";
    if (status === "past_due") return "bg-yellow-100 text-yellow-700 hover:bg-yellow-100";
    if (status === "suspended") return "bg-red-100 text-red-700 hover:bg-red-100";
    return "bg-slate-100 text-slate-700 hover:bg-slate-100";
  }, [dunningStatus]);

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

  const refreshBillingData = async () => {
    const [c, u, i] = await Promise.all([
      getCurrentBillingPlan(),
      getBillingUsage(),
      getBillingInvoices()
    ]);
    setSub(c || null);
      setUsageValues(u?.values || {});
      setCreditBalances(c?.creditBalances || u?.creditBalances || {});
      setInvoices(Array.isArray(i) ? i : []);
  };

  const upgradeWithRazorpay = async (plan) => {
    setPayingCode(plan.code);
    try {
      const cfg = paymentConfig;
      const checkoutKey = cfg?.razorpay?.checkoutKeyId || cfg?.razorpay?.keyId || "";
      if (!cfg?.razorpay?.enabled || !checkoutKey) throw new Error("Razorpay is not configured.");
      const cycle = plan.pricingModel === "usage_pack" ? "usage_based" : "monthly";
      const order = await createRazorpayOrder(plan.code, cycle);
      await ensureRazorpayScript();

      await new Promise((resolve, reject) => {
        const rzp = new window.Razorpay({
          key: order.keyId || checkoutKey,
          amount: order.amount,
          currency: order.currency || "INR",
          order_id: order.orderId,
          name: "Textzy",
          description: plan.pricingModel === "usage_pack" ? `${plan.name} credit purchase` : `${plan.name} plan upgrade`,
          handler: async function (resp) {
            try {
              await verifyRazorpayPayment({
                planCode: plan.code,
                billingCycle: plan.pricingModel === "usage_pack" ? "usage_based" : "monthly",
                razorpayOrderId: resp.razorpay_order_id,
                razorpayPaymentId: resp.razorpay_payment_id,
                razorpaySignature: resp.razorpay_signature
              });
              resolve(true);
            } catch (e) {
              reject(e);
            }
          },
          modal: {
            ondismiss: () => reject(new Error("Payment cancelled."))
          },
          theme: { color: "#f97316" }
        });
        rzp.open();
      });

      toast.success(plan.pricingModel === "usage_pack" ? "Payment successful. Credits added." : "Payment successful. Plan updated.");
      await refreshBillingData();
      setShowUpgradeDialog(false);
    } catch (e) {
      toast.error(e?.message || "Payment failed");
    } finally {
      setPayingCode("");
    }
  };

  const triggerBlobDownload = (blob, filename) => {
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    a.remove();
    window.URL.revokeObjectURL(url);
  };

  const openCompanySettings = () => {
    if (isSuperAdmin && ownerMode === "platform") {
      navigate("/dashboard/platform-branding");
      return;
    }
    navigate("/dashboard/settings?tab=company");
  };

  const handleDownloadInvoice = async (invoice) => {
    try {
      const blob = await downloadBillingInvoice(invoice.id);
      const name = `${invoice.invoiceNo || invoice.id || "invoice"}.html`;
      triggerBlobDownload(blob, name);
    } catch (e) {
      toast.error(e?.message || "Failed to download invoice");
    }
  };

  const handleDownloadAllInvoices = async () => {
    try {
      const blob = await downloadAllBillingInvoices();
      triggerBlobDownload(blob, "textzy-invoices.csv");
    } catch (e) {
      toast.error(e?.message || "Failed to download invoices");
    }
  };

  return (
    <div className="space-y-6" data-testid="billing-page">
      {/* Header */}
      <div className="flex flex-col md:flex-row md:items-center md:justify-between gap-4">
        <div>
          <h1 className="text-2xl font-heading font-bold text-slate-900">Billing</h1>
          <p className="text-slate-600">Manage your subscription and billing details</p>
        </div>
        <Dialog open={showUpgradeDialog} onOpenChange={setShowUpgradeDialog}>
          <DialogTrigger asChild>
            <Button className="bg-orange-500 hover:bg-orange-600 text-white gap-2" data-testid="upgrade-plan-btn">
              <Zap className="w-4 h-4" />
              Upgrade Plan
            </Button>
          </DialogTrigger>
          <DialogContent className="max-w-4xl">
            <DialogHeader>
              <DialogTitle>Choose Your Plan</DialogTitle>
              <DialogDescription>
                Select the plan that best fits your needs
              </DialogDescription>
            </DialogHeader>
            <div className="py-4">
              <div className="grid md:grid-cols-3 gap-4">
                {plans.map((plan, index) => (
                  <Card
                    key={index}
                    className={`border-slate-200 relative ${
                      currentPlan?.code === plan.code ? "border-2 border-orange-500" : ""
                    } ${index === 1 ? "shadow-glow" : ""}`}
                  >
                    {index === 1 && (
                      <div className="absolute -top-3 left-1/2 -translate-x-1/2">
                        <Badge className="bg-orange-500 text-white hover:bg-orange-500">Most Popular</Badge>
                      </div>
                    )}
                    <CardHeader className="pt-8">
                      <CardTitle className="text-lg">{plan.name}</CardTitle>
                      <div className="mt-2">
                        <span className="text-3xl font-bold text-slate-900">{formatMoneyValue(plan.priceMonthly || 0, plan.currency)}</span>
                        <span className="ml-2 text-slate-500">{String(plan.taxMode || "exclusive").toLowerCase() === "inclusive" ? "incl. GST" : "+ GST"}</span>
                      </div>
                      <p className="mt-2 text-sm text-slate-500">{describePlanPrice(plan)}</p>
                    </CardHeader>
                    <CardContent>
                      <ul className="space-y-2 mb-6">
                        {plan.features.map((feature, i) => (
                          <li key={i} className="flex items-center gap-2 text-sm text-slate-600">
                            <Check className="w-4 h-4 text-green-500" />
                            {feature}
                          </li>
                        ))}
                      </ul>
                      <Button
                        className={`w-full ${
                          currentPlan?.code === plan.code
                            ? "bg-slate-100 text-slate-700 hover:bg-slate-200"
                            : "bg-orange-500 hover:bg-orange-600 text-white"
                        }`}
                        disabled={currentPlan?.code === plan.code}
                        onClick={async () => {
                          if (currentPlan?.code === plan.code) return;
                          if (plan.code === "enterprise") return toast.info("Contact sales for enterprise.");
                          try {
                            if ((paymentConfig?.provider || "").toLowerCase() === "razorpay" && paymentConfig?.razorpay?.enabled) {
                              await upgradeWithRazorpay(plan);
                              return;
                            }
                            await changeBillingPlan(plan.code, "monthly");
                            toast.success("Plan changed");
                            await refreshBillingData();
                            setShowUpgradeDialog(false);
                          } catch (e) {
                            toast.error(e?.message || "Failed to change plan");
                          }
                        }}
                      >
                        {currentPlan?.code === plan.code
                          ? "Current Plan"
                          : plan.code === "enterprise"
                          ? "Contact Sales"
                          : payingCode === plan.code
                          ? "Processing..."
                          : (paymentConfig?.provider || "").toLowerCase() === "razorpay" && paymentConfig?.razorpay?.enabled
                          ? "Pay & Upgrade"
                          : "Upgrade"}
                      </Button>
                    </CardContent>
                  </Card>
                ))}
              </div>
            </div>
          </DialogContent>
        </Dialog>
      </div>

      <section className="overflow-hidden rounded-[28px] border border-slate-200 bg-[radial-gradient(circle_at_top_left,_rgba(249,115,22,0.16),_transparent_28%),linear-gradient(135deg,#fff7ed_0%,#ffffff_42%,#f8fafc_100%)] p-6 shadow-sm">
        <div className="grid gap-4 lg:grid-cols-[1.35fr_0.95fr]">
          <div>
            <Badge className="bg-white text-orange-700 hover:bg-white">Billing Overview</Badge>
            <h2 className="mt-4 text-3xl font-bold tracking-tight text-slate-950">
              {currentPlan?.name || "No active"} plan for {companyName}
            </h2>
            <p className="mt-2 max-w-2xl text-sm leading-6 text-slate-600">
              Track your commercial plan, invoice history, usage posture, and support options from one billing workspace.
            </p>
            <div className="mt-5 flex flex-wrap gap-3">
              <Button className="bg-orange-500 text-white hover:bg-orange-600" onClick={() => setShowUpgradeDialog(true)}>
                Upgrade or change plan
              </Button>
              <Button variant="outline" onClick={() => navigate("/dashboard/support")}>
                Create billing ticket
              </Button>
            </div>
          </div>
          <div className="grid gap-3 sm:grid-cols-3 lg:grid-cols-1">
            <div className="rounded-2xl border border-orange-100 bg-white/90 p-4">
              <p className="text-xs font-semibold uppercase tracking-[0.16em] text-slate-500">Current plan</p>
              <p className="mt-2 text-2xl font-bold text-slate-950">{currentPlan?.name || "Not assigned"}</p>
              <p className="mt-1 text-sm text-slate-500">{describePlanPrice(currentPlan, sub?.subscription?.billingCycle || "monthly")}</p>
            </div>
            <div className="rounded-2xl border border-orange-100 bg-white/90 p-4">
              <p className="text-xs font-semibold uppercase tracking-[0.16em] text-slate-500">Invoices</p>
              <p className="mt-2 text-2xl font-bold text-slate-950">{Number(invoiceStats.totalInvoices || 0).toLocaleString("en-IN")}</p>
              <p className="mt-1 text-sm text-slate-500">{Number(invoiceStats.paidInvoices || 0).toLocaleString("en-IN")} paid invoices</p>
            </div>
            <div className="rounded-2xl border border-orange-100 bg-white/90 p-4">
              <p className="text-xs font-semibold uppercase tracking-[0.16em] text-slate-500">Lifetime billed</p>
              <p className="mt-2 text-2xl font-bold text-slate-950">{formatMoneyValue(invoiceStats.totalBilled)}</p>
              <p className="mt-1 text-sm text-slate-500">Across recorded billing invoices</p>
            </div>
          </div>
        </div>
      </section>

      {/* Current Plan Card */}
      <Card className="border-slate-200 shadow-sm">
        <CardContent className="pt-6">
          <div className="flex flex-col md:flex-row md:items-center md:justify-between gap-4">
            <div className="flex items-center gap-4">
              <div className="w-14 h-14 bg-orange-100 rounded-2xl flex items-center justify-center">
                <CreditCard className="w-7 h-7 text-orange-600" />
              </div>
              <div>
                <div className="flex items-center gap-2">
                  <h3 className="text-xl font-semibold text-slate-900">{currentPlan?.name || "Plan"} Plan</h3>
                  <Badge className="bg-green-100 text-green-700 hover:bg-green-100">Active</Badge>
                </div>
                <p className="text-slate-600">
                  {describePlanPrice(currentPlan, sub?.subscription?.billingCycle || "monthly")} | Renews on {sub?.subscription?.renewAtUtc ? new Date(sub.subscription.renewAtUtc).toLocaleDateString() : "-"}
                </p>
              </div>
            </div>
            <div className="flex items-center gap-3">
              <Button variant="outline" data-testid="change-plan-btn" onClick={() => setShowUpgradeDialog(true)}>Change Plan</Button>
              <Button variant="outline" className="text-red-600 hover:text-red-700" data-testid="cancel-plan-btn" onClick={async () => {
                try {
                  await cancelBillingSubscription();
                  toast.success("Subscription cancelled");
                  setSub(await getCurrentBillingPlan());
                } catch {
                  toast.error("Failed to cancel subscription");
                }
              }}>
                Cancel Subscription
              </Button>
            </div>
          </div>
        </CardContent>
      </Card>

      {/* Dunning Status */}
      <Card className="border-slate-200 shadow-sm">
        <CardHeader>
          <CardTitle>Billing Health</CardTitle>
          <CardDescription>Renewal and grace timeline</CardDescription>
        </CardHeader>
        <CardContent>
          {!dunningStatus?.hasSubscription ? (
            <div className="text-sm text-slate-600">No active subscription record found.</div>
          ) : (
            <div className="grid md:grid-cols-4 gap-4">
              <div className="p-4 rounded-xl bg-slate-50 border border-slate-200">
                <div className="text-xs text-slate-500 mb-2">Status</div>
                <Badge className={dunningBadgeClass}>{dunningStatus?.subscription?.status || "unknown"}</Badge>
              </div>
              <div className="p-4 rounded-xl bg-slate-50 border border-slate-200">
                <div className="text-xs text-slate-500 mb-2">Days To Renewal</div>
                <div className="text-xl font-semibold text-slate-900">{Number(dunningStatus?.dunning?.daysToRenew || 0)}</div>
              </div>
              <div className="p-4 rounded-xl bg-slate-50 border border-slate-200">
                <div className="text-xs text-slate-500 mb-2">Past Due Days</div>
                <div className="text-xl font-semibold text-slate-900">{Number(dunningStatus?.dunning?.daysPastDue || 0)}</div>
              </div>
              <div className="p-4 rounded-xl bg-slate-50 border border-slate-200">
                <div className="text-xs text-slate-500 mb-2">Grace Days Left</div>
                <div className="text-xl font-semibold text-slate-900">{Number(dunningStatus?.dunning?.graceDaysLeft || 0)}</div>
              </div>
              <div className="p-4 rounded-xl bg-orange-50 border border-orange-200 md:col-span-4 flex flex-wrap gap-2 items-center">
                <span className="text-xs text-slate-600">Next Action:</span>
                <Badge className="bg-orange-100 text-orange-700 hover:bg-orange-100">
                  {dunningStatus?.dunning?.nextAction || "none"}
                </Badge>
                <span className="text-xs text-slate-600">
                  Renew At: {dunningStatus?.subscription?.renewAtUtc ? new Date(dunningStatus.subscription.renewAtUtc).toLocaleString() : "-"}
                </span>
                <span className="text-xs text-slate-600">
                  Grace Deadline: {dunningStatus?.dunning?.graceDeadlineUtc ? new Date(dunningStatus.dunning.graceDeadlineUtc).toLocaleString() : "-"}
                </span>
              </div>
            </div>
          )}
        </CardContent>
      </Card>

      {/* Usage Section */}
      <Card className="border-slate-200 shadow-sm">
        <CardHeader>
          <CardTitle>Current Usage</CardTitle>
          <CardDescription>Your usage for this billing period</CardDescription>
        </CardHeader>
        <CardContent>
          <div className="grid md:grid-cols-2 lg:grid-cols-4 gap-6">
            {[
              { key: "whatsapp", label: "WhatsApp Messages", icon: MessageSquare, color: "text-green-600", suffix: "messages" },
              { key: "sms", label: "SMS Credits", icon: MessageSquare, color: "text-orange-600", suffix: "credits" },
              { key: "contacts", label: "Contacts", icon: Users, color: "text-blue-600", suffix: "contacts" },
              { key: "team", label: "Team Members", icon: Users, color: "text-purple-600", suffix: "members" },
              { key: "flows", label: "Flows", icon: Workflow, color: "text-indigo-600", suffix: "flows" },
              { key: "chatbots", label: "Chatbots", icon: Bot, color: "text-pink-600", suffix: "bots" },
              { key: "apiCalls", label: "API Calls", icon: Activity, color: "text-cyan-600", suffix: "calls" },
            ].map((item) => {
              const row = usage[item.key];
              const Icon = item.icon;
              return (
                <div className="space-y-3" key={item.key}>
                  <div className="flex items-center justify-between">
                    <div className="flex items-center gap-2">
                      <Icon className={`w-4 h-4 ${item.color}`} />
                      <span className="text-sm font-medium text-slate-700">{item.label}</span>
                    </div>
                    <span className="text-sm text-slate-600">{row.percentage}%</span>
                  </div>
                  <Progress value={row.percentage} className="h-2" />
                  <p className="text-xs text-slate-500">
                    {Number(row.used || 0).toLocaleString()} / {Number(row.limit || 0).toLocaleString()} {item.suffix}
                  </p>
                  {row.percentage > 80 ? (
                    <div className="flex items-center gap-1 text-xs text-yellow-600">
                      <AlertTriangle className="w-3 h-3" />
                      Running low
                    </div>
                  ) : null}
                </div>
              );
            })}
          </div>

          <div className="mt-6 p-4 bg-orange-50 rounded-lg flex items-center justify-between">
            <div className="flex items-center gap-3">
              <Zap className="w-5 h-5 text-orange-600" />
              <div>
                <p className="font-medium text-slate-900">Need more capacity?</p>
                <p className="text-sm text-slate-600">Upgrade to Enterprise for unlimited messages</p>
              </div>
            </div>
            <Button className="bg-orange-500 hover:bg-orange-600 text-white">
              Contact Sales
            </Button>
          </div>
        </CardContent>
      </Card>

      {/* Support, Billing Address & Invoices */}
      <div className="grid lg:grid-cols-3 gap-6">
        <SupportContactCard
          support={supportContext?.support}
          project={supportContext?.project}
          onCreateTicket={() => navigate("/dashboard/support")}
          onOpenSupportDesk={() => navigate("/dashboard/support")}
          compact
        />

        {/* Billing Address */}
        <Card className="border-slate-200 shadow-sm">
          <CardHeader>
            <CardTitle>Billing Address</CardTitle>
            <CardDescription>Your billing information</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="text-sm text-slate-600 space-y-1">
              <p className="font-medium text-slate-900">{companyName}</p>
              <p>{legalName || "Legal name not configured"}</p>
              <p>{billingAddressText || "Address not configured"}</p>
              <p>{billingEmail || "Billing email not configured"}</p>
              <p>{billingPhone || "Billing phone not configured"}</p>
              <p className="pt-2">GSTIN: {gstin || "-"}</p>
              <p>PAN: {pan || "-"}</p>
            </div>
            <Button
              variant="outline"
              className="w-full"
              data-testid="update-address-btn"
              onClick={openCompanySettings}
            >
              Open Company Settings
            </Button>
          </CardContent>
        </Card>
        {/* Quick Stats */}
        <Card className="border-slate-200 shadow-sm">
          <CardHeader>
            <CardTitle>This Month</CardTitle>
            <CardDescription>Current billing period stats</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="flex justify-between items-center">
              <span className="text-slate-600">{taxMode === "inclusive" ? "Plan Cost (incl. GST)" : "Plan Cost (base)"}</span>
              <span className="font-medium text-slate-900">{formatMoneyValue(planCost)}</span>
            </div>
            <div className="flex justify-between items-center">
              <span className="text-slate-600">Additional SMS</span>
              <span className="font-medium text-slate-900">{"\u20B9"}0</span>
            </div>
            <div className="flex justify-between items-center">
              <span className="text-slate-600">
                Taxes ({company?.isTaxExempt ? "Tax Exempt" : `${taxRatePercent}% ${company?.isReverseCharge ? "Reverse Charge" : "GST"}`})
              </span>
              <span className="font-medium text-slate-900">{"\u20B9"}{taxAmount.toLocaleString()}</span>
            </div>
            <div className="border-t border-slate-200 pt-4 flex justify-between items-center">
              <span className="font-medium text-slate-900">Total</span>
              <span className="text-xl font-bold text-slate-900">{formatMoneyValue(totalAmount)}</span>
            </div>
            {taxMode === "exclusive" ? (
              <p className="text-xs text-slate-500">Displayed plan price excludes GST. GST is added on top during invoice and checkout.</p>
            ) : (
              <p className="text-xs text-slate-500">Displayed plan price already includes GST.</p>
            )}
          </CardContent>
        </Card>
      </div>

      {/* Invoices */}
      <Card className="border-slate-200 shadow-sm">
        <CardHeader>
          <div className="flex items-center justify-between">
            <div>
              <CardTitle>Invoice History</CardTitle>
              <CardDescription>Download your past invoices</CardDescription>
            </div>
            <Button variant="outline" className="gap-2" data-testid="download-all-invoices-btn" onClick={handleDownloadAllInvoices}>
              <Download className="w-4 h-4" />
              Download All
            </Button>
          </div>
        </CardHeader>
        <CardContent>
          <div className="overflow-hidden rounded-2xl border border-slate-200">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Invoice</TableHead>
                <TableHead>Date</TableHead>
                <TableHead>Service</TableHead>
                <TableHead>Amount</TableHead>
                <TableHead>Status</TableHead>
                <TableHead className="text-right">Actions</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {invoices.map((invoice) => (
                <TableRow key={invoice.id || invoice.invoiceNo}>
                  <TableCell className="font-medium">{invoice.invoiceNo || invoice.id}</TableCell>
                  <TableCell className="text-slate-600">{invoice.createdAtUtc ? new Date(invoice.createdAtUtc).toLocaleDateString() : "-"}</TableCell>
                  <TableCell className="text-slate-600">{invoice.description || invoice.billingCycle || "-"}</TableCell>
                  <TableCell className="text-slate-900">{formatMoneyValue(invoice.total || 0)}</TableCell>
                  <TableCell>
                    <Badge className={String(invoice.status || "").toLowerCase() === "paid" ? "bg-green-100 text-green-700 hover:bg-green-100" : "bg-slate-100 text-slate-700 hover:bg-slate-100"}>
                      {invoice.status}
                    </Badge>
                  </TableCell>
                  <TableCell className="text-right">
                    <Button variant="ghost" size="sm" className="gap-1" data-testid={`download-invoice-${invoice.invoiceNo || invoice.id}`} onClick={() => handleDownloadInvoice(invoice)}>
                      <Download className="w-4 h-4" />
                      Download
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
              {!invoices.length ? (
                <TableRow>
                  <TableCell colSpan={6} className="py-10 text-center text-slate-500">
                    No invoice history available yet.
                  </TableCell>
                </TableRow>
              ) : null}
            </TableBody>
          </Table>
          </div>
        </CardContent>
      </Card>
    </div>
  );
};

export default BillingPage;

