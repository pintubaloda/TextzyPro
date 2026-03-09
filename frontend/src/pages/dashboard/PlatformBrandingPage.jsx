import { useEffect, useMemo, useState } from "react";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { MessageSquare } from "lucide-react";
import { toast } from "sonner";
import { getPlatformSettings, savePlatformSettings } from "@/lib/api";

const DEFAULTS = {
  platformName: "Textzy",
  logoUrl: "",
  legalName: "TEXTZY DIGITAL SOLUTIONS PRIVATE LIMITED",
  gstin: "27AAFCU5055K1ZO",
  pan: "AAFCU5055K",
  cin: "U74900MH2020PTC345678",
  billingEmail: "",
  billingPhone: "",
  supportEmail: "",
  supportPhone: "",
  website: "",
  address: "Plot No. 456, Tech Park Building, Bandra Kurla Complex\nMumbai, Maharashtra 400051, India",
  invoiceFooter: "",
};

export default function PlatformBrandingPage() {
  const [form, setForm] = useState(DEFAULTS);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);

  const displayName = useMemo(
    () => String(form.platformName || "").trim() || "Textzy",
    [form.platformName],
  );

  useEffect(() => {
    let active = true;
    (async () => {
      try {
        setLoading(true);
        const res = await getPlatformSettings("platform-branding");
        const values = res?.values || {};
        if (!active) return;
        setForm((prev) => ({
          ...prev,
          platformName: values.platformName || prev.platformName,
          logoUrl: values.logoUrl || "",
          legalName: values.legalName || "",
          gstin: values.gstin || "",
          pan: values.pan || "",
          cin: values.cin || "",
          billingEmail: values.billingEmail || "",
          billingPhone: values.billingPhone || "",
          supportEmail: values.supportEmail || "",
          supportPhone: values.supportPhone || "",
          website: values.website || "",
          address: values.address || "",
          invoiceFooter: values.invoiceFooter || "",
        }));
      } catch (e) {
        toast.error(e?.message || "Failed to load platform branding");
      } finally {
        if (active) setLoading(false);
      }
    })();
    return () => {
      active = false;
    };
  }, []);

  const update = (key, value) => setForm((prev) => ({ ...prev, [key]: value }));

  const handleSave = async () => {
    try {
      setSaving(true);
      await savePlatformSettings("platform-branding", {
        platformName: form.platformName,
        logoUrl: form.logoUrl,
        legalName: form.legalName,
        gstin: form.gstin,
        pan: form.pan,
        cin: form.cin,
        billingEmail: form.billingEmail,
        billingPhone: form.billingPhone,
        supportEmail: form.supportEmail,
        supportPhone: form.supportPhone,
        website: form.website,
        address: form.address,
        invoiceFooter: form.invoiceFooter,
      });
      toast.success("Platform branding saved");
    } catch (e) {
      toast.error(e?.message || "Failed to save platform branding");
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="space-y-6" data-testid="platform-branding-page">
      <div>
        <h1 className="text-2xl font-heading font-bold text-slate-900">Platform Branding</h1>
        <p className="text-slate-600">Configure platform name, logo and legal billing details used in platform screens.</p>
      </div>

      <Card>
        <CardHeader>
          <CardTitle>Preview</CardTitle>
          <CardDescription>How platform header branding will appear</CardDescription>
        </CardHeader>
        <CardContent>
          <div className="flex items-center gap-3 rounded-xl border border-slate-200 bg-white p-4">
            {form.logoUrl ? (
              <img src={form.logoUrl} alt={displayName} className="h-10 w-10 rounded-lg object-cover" />
            ) : (
              <div className="h-10 w-10 rounded-lg bg-orange-500 flex items-center justify-center">
                <MessageSquare className="h-5 w-5 text-white" />
              </div>
            )}
            <div className="min-w-0">
              <p className="font-semibold text-slate-900 truncate">{displayName}</p>
              <div className="flex items-center gap-2 mt-1">
                <Badge variant="outline">Platform</Badge>
                {form.legalName ? <span className="text-xs text-slate-500 truncate">{form.legalName}</span> : null}
              </div>
            </div>
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Brand Details</CardTitle>
        </CardHeader>
        <CardContent className="grid md:grid-cols-2 gap-4">
          <div className="space-y-2">
            <Label>Platform Name</Label>
            <Input value={form.platformName} onChange={(e) => update("platformName", e.target.value)} placeholder="Textzy" />
          </div>
          <div className="space-y-2">
            <Label>Logo URL</Label>
            <Input value={form.logoUrl} onChange={(e) => update("logoUrl", e.target.value)} placeholder="https://..." />
          </div>
          <div className="space-y-2">
            <Label>Legal Company Name</Label>
            <Input value={form.legalName} onChange={(e) => update("legalName", e.target.value)} placeholder="Moneyart Private Limited" />
          </div>
          <div className="space-y-2">
            <Label>Website</Label>
            <Input value={form.website} onChange={(e) => update("website", e.target.value)} placeholder="https://..." />
          </div>
          <div className="space-y-2">
            <Label>GSTIN</Label>
            <Input value={form.gstin} onChange={(e) => update("gstin", e.target.value)} placeholder="22AAAAA0000A1Z5" />
          </div>
          <div className="space-y-2">
            <Label>PAN</Label>
            <Input value={form.pan} onChange={(e) => update("pan", e.target.value)} placeholder="AAAAA0000A" />
          </div>
          <div className="space-y-2">
            <Label>CIN</Label>
            <Input value={form.cin} onChange={(e) => update("cin", e.target.value)} placeholder="U74900MH2020PTC345678" />
          </div>
          <div className="space-y-2">
            <Label>Billing Email</Label>
            <Input value={form.billingEmail} onChange={(e) => update("billingEmail", e.target.value)} placeholder="billing@domain.com" />
          </div>
          <div className="space-y-2">
            <Label>Billing Phone</Label>
            <Input value={form.billingPhone} onChange={(e) => update("billingPhone", e.target.value)} placeholder="+91..." />
          </div>
          <div className="space-y-2">
            <Label>Support Email</Label>
            <Input value={form.supportEmail} onChange={(e) => update("supportEmail", e.target.value)} placeholder="support@domain.com" />
          </div>
          <div className="space-y-2">
            <Label>Support WhatsApp No.</Label>
            <Input value={form.supportPhone} onChange={(e) => update("supportPhone", e.target.value)} placeholder="+91..." />
          </div>
          <div className="space-y-2 md:col-span-2">
            <Label>Billing Address</Label>
            <Textarea value={form.address} onChange={(e) => update("address", e.target.value)} rows={3} placeholder="Full billing address" />
          </div>
          <div className="space-y-2 md:col-span-2">
            <Label>Invoice Footer</Label>
            <Textarea value={form.invoiceFooter} onChange={(e) => update("invoiceFooter", e.target.value)} rows={3} placeholder="Thank you for doing business with us." />
          </div>
        </CardContent>
      </Card>

      <div className="flex justify-end">
        <Button className="bg-orange-500 hover:bg-orange-600 text-white" onClick={handleSave} disabled={loading || saving}>
          {saving ? "Saving..." : "Save Branding"}
        </Button>
      </div>
    </div>
  );
}
