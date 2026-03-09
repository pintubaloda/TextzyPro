import { useEffect, useMemo, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Label } from "@/components/ui/label";
import { Input } from "@/components/ui/input";
import { Button } from "@/components/ui/button";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Badge } from "@/components/ui/badge";
import {
  addSmsOptOut,
  archiveSmsSender,
  createSmsSender,
  createSmsTemplate,
  deleteSmsTemplate,
  getSmsComplianceKpis,
  getSmsSenderStats,
  importApprovedSmsTemplatesCsv,
  listSmsBillingLedger,
  listSmsComplianceEvents,
  listSmsOptOuts,
  listSmsSenders,
  listSmsTemplates,
  listTenantSmsGatewayReport,
  removeSmsOptOut,
  sendSmsTestFromDashboard,
  setSmsTemplateStatus,
  getTenantSmsGatewayReportStatus,
  updateSmsSender,
  updateSmsTemplate,
} from "@/lib/api";
import { toast } from "sonner";
import { CheckCircle2, FileCheck2, ShieldCheck, Trash2, MessageSquareText, Ban, Activity, Eye, Upload, Download } from "lucide-react";

const defaultSenderForm = {
  senderId: "",
  entityId: "",
  routeType: "service_explicit",
  purpose: "",
  description: "",
  isVerified: false,
};

const defaultTemplateForm = {
  name: "",
  category: "service",
  language: "en",
  body: "",
  smsSenderId: "",
  dltEntityId: "",
  dltTemplateId: "",
  smsOperator: "all",
  effectiveFromUtc: "",
  effectiveToUtc: "",
};

const SmsSetupPage = () => {
  const [searchParams, setSearchParams] = useSearchParams();
  const [panel, setPanel] = useState(searchParams.get("panel") || "senders");

  const [senders, setSenders] = useState([]);
  const [senderStats, setSenderStats] = useState({ total: 0, verified: 0, compliant: 0, byRoute: {} });
  const [senderForm, setSenderForm] = useState(defaultSenderForm);
  const [editingSenderId, setEditingSenderId] = useState("");

  const [templateRows, setTemplateRows] = useState([]);
  const [templateForm, setTemplateForm] = useState(defaultTemplateForm);
  const [editingTemplateId, setEditingTemplateId] = useState("");
  const [viewTemplate, setViewTemplate] = useState(null);
  const [importFile, setImportFile] = useState(null);
  const [importResult, setImportResult] = useState(null);

  const [optOutRows, setOptOutRows] = useState([]);
  const [optOutPhone, setOptOutPhone] = useState("");
  const [optOutReason, setOptOutReason] = useState("");

  const [events, setEvents] = useState([]);
  const [billingRows, setBillingRows] = useState([]);
  const [gatewayReportRows, setGatewayReportRows] = useState([]);
  const [gatewayReportEnabled, setGatewayReportEnabled] = useState(false);
  const [kpis, setKpis] = useState({ templatesTotal: 0, templatesApproved: 0, templatesPending: 0, templatesRejected: 0, optOuts: 0, sentToday: 0, deliveredToday: 0, failedToday: 0, billedToday: 0 });

  const [busy, setBusy] = useState(false);
  const [testSmsBusy, setTestSmsBusy] = useState(false);
  const [testSmsForm, setTestSmsForm] = useState({
    phone: "",
    useTemplate: false,
    message: "",
    templateName: "",
    templateLanguageCode: "en",
    templateParametersCsv: "",
  });

  const loadAll = async () => {
    try {
      const [senderRes, senderStatRes, tplRes, optRes, eventRes, billingRes, kpiRes, reportStatus] = await Promise.all([
        listSmsSenders().catch(() => []),
        getSmsSenderStats().catch(() => null),
        listSmsTemplates().catch(() => []),
        listSmsOptOuts(400).catch(() => []),
        listSmsComplianceEvents(200).catch(() => []),
        listSmsBillingLedger(300).catch(() => []),
        getSmsComplianceKpis().catch(() => null),
        getTenantSmsGatewayReportStatus().catch(() => ({ enabled: false })),
      ]);
      setSenders(senderRes || []);
      if (senderStatRes) setSenderStats(senderStatRes);
      setTemplateRows(tplRes || []);
      setOptOutRows(optRes || []);
      setEvents(eventRes || []);
      setBillingRows(billingRes || []);
      setGatewayReportEnabled(!!reportStatus?.enabled);
      if (reportStatus?.enabled) {
        const logs = await listTenantSmsGatewayReport({ limit: 200 }).catch(() => []);
        setGatewayReportRows(Array.isArray(logs) ? logs : []);
      } else {
        setGatewayReportRows([]);
      }
      if (kpiRes) setKpis(kpiRes);
    } catch {
      toast.error("Failed to load SMS module data.");
    }
  };

  useEffect(() => {
    loadAll();
  }, []);

  useEffect(() => {
    const next = searchParams.get("panel") || "senders";
    setPanel(next);
  }, [searchParams]);

  const routeMix = senderStats?.byRoute || {};

  const saveSender = async () => {
    if (!senderForm.senderId.trim() || !senderForm.entityId.trim()) {
      toast.error("Sender ID and Entity ID are required.");
      return;
    }
    try {
      setBusy(true);
      const payload = {
        senderId: senderForm.senderId.trim().toUpperCase(),
        entityId: senderForm.entityId.trim(),
        routeType: senderForm.routeType,
        purpose: senderForm.purpose.trim(),
        description: senderForm.description.trim(),
        isVerified: !!senderForm.isVerified,
      };
      if (editingSenderId) {
        await updateSmsSender(editingSenderId, payload);
        toast.success("SMS sender updated.");
      } else {
        await createSmsSender(payload);
        toast.success("SMS sender created.");
      }
      setEditingSenderId("");
      setSenderForm(defaultSenderForm);
      await loadAll();
    } catch (e) {
      toast.error(e?.message || "Failed to save sender.");
    } finally {
      setBusy(false);
    }
  };

  const saveTemplate = async () => {
    if (!templateForm.name.trim() || !templateForm.body.trim() || !templateForm.smsSenderId.trim() || !templateForm.dltEntityId.trim() || !templateForm.dltTemplateId.trim()) {
      toast.error("Name, body, sender ID, entity ID and DLT template ID are required.");
      return;
    }

    const payload = {
      ...templateForm,
      name: templateForm.name.trim(),
      body: templateForm.body.trim(),
      smsSenderId: templateForm.smsSenderId.trim().toUpperCase(),
      dltEntityId: templateForm.dltEntityId.trim(),
      dltTemplateId: templateForm.dltTemplateId.trim(),
      effectiveFromUtc: templateForm.effectiveFromUtc ? new Date(templateForm.effectiveFromUtc).toISOString() : null,
      effectiveToUtc: templateForm.effectiveToUtc ? new Date(templateForm.effectiveToUtc).toISOString() : null,
    };

    try {
      setBusy(true);
      if (editingTemplateId) {
        await updateSmsTemplate(editingTemplateId, payload);
        toast.success("Template updated.");
      } else {
        await createSmsTemplate(payload);
        toast.success("Template created.");
      }
      setEditingTemplateId("");
      setTemplateForm(defaultTemplateForm);
      await loadAll();
    } catch (e) {
      toast.error(e?.message || "Failed to save template.");
    } finally {
      setBusy(false);
    }
  };

  const handleDeleteTemplate = async (id) => {
    if (!id) return;
    if (!window.confirm("Delete this SMS template?")) return;
    try {
      setBusy(true);
      await deleteSmsTemplate(id);
      toast.success("Template deleted.");
      if (editingTemplateId === id) {
        setEditingTemplateId("");
        setTemplateForm(defaultTemplateForm);
      }
      if (viewTemplate?.id === id) setViewTemplate(null);
      await loadAll();
    } catch (e) {
      toast.error(e?.message || "Failed to delete template.");
    } finally {
      setBusy(false);
    }
  };

  const downloadTemplateCsvSample = () => {
    const sample = [
      "EntityID,TemplateName,TemplateID,TemplateContent,Header,TemplateType,SenderID,Operator",
      "1401234567890123456,otp_login,TMP10001,\"Your OTP is {{1}}\",OTP Alert,otp,TXTZY,all"
    ].join("\n");
    const blob = new Blob([sample], { type: "text/csv;charset=utf-8;" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = "sms-approved-templates-sample.csv";
    a.click();
    URL.revokeObjectURL(url);
  };

  const handleImportApprovedCsv = async () => {
    if (!importFile) {
      toast.error("Please choose a CSV file.");
      return;
    }
    try {
      setBusy(true);
      const result = await importApprovedSmsTemplatesCsv(importFile);
      setImportResult(result);
      toast.success(`Import done. Imported: ${result.imported || 0}, Updated: ${result.updated || 0}, Rejected: ${result.rejected || 0}`);
      setImportFile(null);
      await loadAll();
    } catch (e) {
      toast.error(e?.message || "Failed to import CSV.");
    } finally {
      setBusy(false);
    }
  };

  const addOptOut = async () => {
    if (!optOutPhone.trim()) {
      toast.error("Phone is required.");
      return;
    }
    try {
      setBusy(true);
      await addSmsOptOut({ phone: optOutPhone.trim(), reason: optOutReason.trim(), source: "manual" });
      toast.success("Phone added to opt-out list.");
      setOptOutPhone("");
      setOptOutReason("");
      await loadAll();
    } catch (e) {
      toast.error(e?.message || "Failed to add opt-out.");
    } finally {
      setBusy(false);
    }
  };

  const statusBadge = (value) => {
    const v = String(value || "").toLowerCase();
    if (v === "delivered") return <Badge className="bg-green-100 text-green-700 hover:bg-green-100">Delivered</Badge>;
    if (v === "failed") return <Badge className="bg-red-100 text-red-700 hover:bg-red-100">Failed</Badge>;
    if (v === "queued" || v === "processing" || v === "submitted") return <Badge className="bg-amber-100 text-amber-700 hover:bg-amber-100">In Progress</Badge>;
    if (v === "approved") return <Badge className="bg-green-100 text-green-700 hover:bg-green-100">Approved</Badge>;
    if (v === "inreview") return <Badge className="bg-amber-100 text-amber-700 hover:bg-amber-100">Pending</Badge>;
    if (v === "rejected") return <Badge className="bg-red-100 text-red-700 hover:bg-red-100">Rejected</Badge>;
    if (v === "expired") return <Badge variant="secondary">Expired</Badge>;
    return <Badge variant="outline">Draft</Badge>;
  };

  const statusOptions = useMemo(() => ["draft", "submitted", "approved", "rejected", "expired"], []);

  const sendTestSms = async () => {
    if (!testSmsForm.phone.trim()) {
      toast.error("Phone is required.");
      return;
    }
    if (!testSmsForm.useTemplate && !testSmsForm.message.trim()) {
      toast.error("Message is required.");
      return;
    }
    if (testSmsForm.useTemplate && !testSmsForm.templateName.trim()) {
      toast.error("Template name is required.");
      return;
    }
    try {
      setTestSmsBusy(true);
      const templateParameters = testSmsForm.templateParametersCsv
        .split(",")
        .map((x) => x.trim())
        .filter(Boolean);
      const res = await sendSmsTestFromDashboard({
        phone: testSmsForm.phone.trim(),
        message: testSmsForm.message.trim(),
        useTemplate: testSmsForm.useTemplate,
        templateName: testSmsForm.templateName.trim(),
        templateLanguageCode: testSmsForm.templateLanguageCode.trim() || "en",
        templateParameters,
      });
      toast.success(`Test SMS queued. Message ID: ${res?.id || "-"}`);
    } catch (e) {
      toast.error(e?.message || "Failed to send test SMS.");
    } finally {
      setTestSmsBusy(false);
    }
  };

  return (
    <div className="space-y-6" data-testid="sms-setup-page">
      <div>
        <h1 className="text-2xl font-heading font-bold text-slate-900">SMS Control Center</h1>
        <p className="text-slate-600">Professional DLT-ready SMS operations: senders, template lifecycle, opt-outs, and delivery observability.</p>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-6 gap-4">
        <Card className="border-slate-200 bg-gradient-to-r from-orange-50 to-white"><CardContent className="pt-6"><div className="flex items-center gap-3"><ShieldCheck className="w-5 h-5 text-orange-500" /><div><p className="text-xs text-slate-500">Senders</p><p className="text-2xl font-semibold text-slate-900">{senderStats?.total || 0}</p></div></div></CardContent></Card>
        <Card className="border-slate-200"><CardContent className="pt-6"><div className="flex items-center gap-3"><CheckCircle2 className="w-5 h-5 text-green-600" /><div><p className="text-xs text-slate-500">Approved Templates</p><p className="text-2xl font-semibold text-slate-900">{kpis.templatesApproved || 0}</p></div></div></CardContent></Card>
        <Card className="border-slate-200"><CardContent className="pt-6"><div className="flex items-center gap-3"><FileCheck2 className="w-5 h-5 text-blue-600" /><div><p className="text-xs text-slate-500">Templates Total</p><p className="text-2xl font-semibold text-slate-900">{kpis.templatesTotal || 0}</p></div></div></CardContent></Card>
        <Card className="border-slate-200"><CardContent className="pt-6"><div className="flex items-center gap-3"><Ban className="w-5 h-5 text-red-600" /><div><p className="text-xs text-slate-500">Opt-Outs</p><p className="text-2xl font-semibold text-slate-900">{kpis.optOuts || 0}</p></div></div></CardContent></Card>
        <Card className="border-slate-200"><CardContent className="pt-6"><div className="flex items-center gap-3"><MessageSquareText className="w-5 h-5 text-indigo-600" /><div><p className="text-xs text-slate-500">SMS Sent Today</p><p className="text-2xl font-semibold text-slate-900">{kpis.sentToday || 0}</p></div></div></CardContent></Card>
        <Card className="border-slate-200"><CardContent className="pt-6"><div className="flex items-center gap-3"><Activity className="w-5 h-5 text-amber-600" /><div><p className="text-xs text-slate-500">Delivered / Failed</p><p className="text-2xl font-semibold text-slate-900">{kpis.deliveredToday || 0} / {kpis.failedToday || 0}</p></div></div></CardContent></Card>
      </div>
      <Card className="border-slate-200"><CardContent className="pt-4"><p className="text-xs text-slate-500">Billed Today</p><p className="text-2xl font-semibold text-slate-900">INR {Number(kpis.billedToday || 0).toFixed(2)}</p></CardContent></Card>

      <div className="flex flex-wrap gap-2">
        {[{k:"senders",l:"DLT Senders"},{k:"templates",l:"Template Registry"},{k:"optouts",l:"Opt-Out Control"},{k:"events",l:"Delivery Events"},{k:"billing",l:"Billing Ledger"},{k:"gateway",l:"Message Report"}].map((x)=>(
          <Button key={x.k} variant={panel===x.k?"default":"outline"} className={panel===x.k?"bg-orange-500 hover:bg-orange-600":""} onClick={()=>{ setPanel(x.k); setSearchParams({ panel: x.k }); }}>{x.l}</Button>
        ))}
      </div>

      <Card className="border-slate-200">
        <CardHeader>
          <CardTitle className="text-base">Send Test SMS</CardTitle>
        </CardHeader>
        <CardContent className="grid grid-cols-1 md:grid-cols-4 gap-3">
          <div className="space-y-2">
            <Label>Phone</Label>
            <Input
              placeholder="+919876543210"
              value={testSmsForm.phone}
              onChange={(e) => setTestSmsForm((p) => ({ ...p, phone: e.target.value }))}
            />
          </div>
          <div className="space-y-2">
            <Label>Mode</Label>
            <Select
              value={testSmsForm.useTemplate ? "template" : "text"}
              onValueChange={(v) => setTestSmsForm((p) => ({ ...p, useTemplate: v === "template" }))}
            >
              <SelectTrigger><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="text">Text</SelectItem>
                <SelectItem value="template">Template</SelectItem>
              </SelectContent>
            </Select>
          </div>
          {!testSmsForm.useTemplate ? (
            <div className="space-y-2 md:col-span-2">
              <Label>Message</Label>
              <Input
                placeholder="Type test SMS text"
                value={testSmsForm.message}
                onChange={(e) => setTestSmsForm((p) => ({ ...p, message: e.target.value }))}
              />
            </div>
          ) : (
            <>
              <div className="space-y-2">
                <Label>Template Name</Label>
                <Input
                  placeholder="otp_login"
                  value={testSmsForm.templateName}
                  onChange={(e) => setTestSmsForm((p) => ({ ...p, templateName: e.target.value }))}
                />
              </div>
              <div className="space-y-2">
                <Label>Language</Label>
                <Input
                  placeholder="en"
                  value={testSmsForm.templateLanguageCode}
                  onChange={(e) => setTestSmsForm((p) => ({ ...p, templateLanguageCode: e.target.value }))}
                />
              </div>
              <div className="space-y-2 md:col-span-2">
                <Label>Template Parameters (comma separated)</Label>
                <Input
                  placeholder="123456, John"
                  value={testSmsForm.templateParametersCsv}
                  onChange={(e) => setTestSmsForm((p) => ({ ...p, templateParametersCsv: e.target.value }))}
                />
              </div>
            </>
          )}
          <div>
            <Button className="bg-orange-500 hover:bg-orange-600 w-full" disabled={testSmsBusy} onClick={sendTestSms}>
              {testSmsBusy ? "Sending..." : "Send Test SMS"}
            </Button>
          </div>
        </CardContent>
      </Card>

      {panel === "senders" && (
        <>
          <Card className="border-slate-200">
            <CardHeader><CardTitle className="text-base">{editingSenderId ? "Edit DLT Sender" : "Add DLT Sender"}</CardTitle></CardHeader>
            <CardContent className="grid grid-cols-1 md:grid-cols-3 gap-3">
              <div className="space-y-2"><Label>Sender ID</Label><Input placeholder="TXTZY" value={senderForm.senderId} onChange={(e)=>setSenderForm((p)=>({ ...p, senderId:e.target.value.toUpperCase() }))} /></div>
              <div className="space-y-2"><Label>DLT Entity ID</Label><Input placeholder="19-digit PE ID" value={senderForm.entityId} onChange={(e)=>setSenderForm((p)=>({ ...p, entityId:e.target.value }))} /></div>
              <div className="space-y-2"><Label>Route Type</Label><Select value={senderForm.routeType} onValueChange={(v)=>setSenderForm((p)=>({ ...p, routeType:v }))}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent><SelectItem value="service_explicit">Service Explicit</SelectItem><SelectItem value="service_implicit">Service Implicit</SelectItem><SelectItem value="transactional">Transactional</SelectItem><SelectItem value="promotional">Promotional</SelectItem></SelectContent></Select></div>
              <div className="space-y-2 md:col-span-2"><Label>Purpose</Label><Input placeholder="OTP / alerts / reminders" value={senderForm.purpose} onChange={(e)=>setSenderForm((p)=>({ ...p, purpose:e.target.value }))} /></div>
              <div className="space-y-2"><Label>Verification</Label><Select value={senderForm.isVerified?"yes":"no"} onValueChange={(v)=>setSenderForm((p)=>({ ...p, isVerified:v==="yes" }))}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent><SelectItem value="yes">Verified</SelectItem><SelectItem value="no">Pending</SelectItem></SelectContent></Select></div>
              <div className="space-y-2 md:col-span-3"><Label>Description</Label><Input placeholder="Internal note" value={senderForm.description} onChange={(e)=>setSenderForm((p)=>({ ...p, description:e.target.value }))} /></div>
              <div><Button className="bg-orange-500 hover:bg-orange-600 w-full" disabled={busy} onClick={saveSender}>{editingSenderId?"Update Sender":"Save Sender"}</Button></div>
              {editingSenderId ? <div><Button variant="outline" className="w-full" onClick={()=>{setEditingSenderId("");setSenderForm(defaultSenderForm);}}>Cancel Edit</Button></div> : null}
            </CardContent>
          </Card>

          <Card className="border-slate-200"><CardHeader><CardTitle className="text-base">Saved Sender IDs</CardTitle></CardHeader><CardContent><Table><TableHeader><TableRow><TableHead>Sender ID</TableHead><TableHead>Entity ID</TableHead><TableHead>Route</TableHead><TableHead>Status</TableHead><TableHead>Created</TableHead><TableHead className="text-right">Actions</TableHead></TableRow></TableHeader><TableBody>{senders.length===0?<TableRow><TableCell colSpan={6} className="text-slate-500">No sender IDs configured.</TableCell></TableRow>:null}{senders.map((r)=><TableRow key={r.id}><TableCell className="font-mono">{r.senderId}</TableCell><TableCell className="font-mono">{r.entityId}</TableCell><TableCell><Badge variant="outline">{r.routeType || "service_explicit"}</Badge></TableCell><TableCell>{r.isVerified ? <Badge className="bg-green-100 text-green-700 hover:bg-green-100">Verified</Badge> : <Badge variant="secondary">Pending</Badge>}</TableCell><TableCell>{r.createdAtUtc ? new Date(r.createdAtUtc).toLocaleString() : "-"}</TableCell><TableCell className="text-right"><div className="inline-flex gap-2"><Button size="sm" variant="outline" onClick={()=>{setEditingSenderId(r.id);setSenderForm({senderId:r.senderId||"",entityId:r.entityId||"",routeType:r.routeType||"service_explicit",purpose:r.purpose||"",description:r.description||"",isVerified:!!r.isVerified});}}>Edit</Button><Button size="sm" variant="ghost" className="text-red-600" onClick={async()=>{await archiveSmsSender(r.id);toast.success("Sender archived.");await loadAll();}}><Trash2 className="w-4 h-4 mr-1" />Archive</Button></div></TableCell></TableRow>)}</TableBody></Table></CardContent></Card>

          <Card className="border-slate-200"><CardContent className="pt-4"><p className="text-xs text-slate-500 mb-2">Route Mix</p><div className="flex flex-wrap gap-2">{Object.keys(routeMix).length===0?<Badge variant="secondary">No data</Badge>:null}{Object.entries(routeMix).map(([k,v])=><Badge key={k} variant="outline">{k}: {v}</Badge>)}</div></CardContent></Card>
        </>
      )}

      {panel === "templates" && (
        <>
          <Card className="border-orange-200 bg-gradient-to-r from-orange-50 to-amber-50">
            <CardHeader>
              <CardTitle className="text-base">Approved DLT CSV Import</CardTitle>
            </CardHeader>
            <CardContent className="grid grid-cols-1 md:grid-cols-4 gap-3">
              <div className="md:col-span-2 space-y-2">
                <Label>Upload Approved Template CSV</Label>
                <Input
                  type="file"
                  accept=".csv,text/csv"
                  onChange={(e) => setImportFile(e.target.files?.[0] || null)}
                />
              </div>
              <div className="flex items-end">
                <Button variant="outline" className="w-full" onClick={downloadTemplateCsvSample}>
                  <Download className="w-4 h-4 mr-2" /> Sample CSV
                </Button>
              </div>
              <div className="flex items-end">
                <Button className="w-full bg-orange-500 hover:bg-orange-600" disabled={busy} onClick={handleImportApprovedCsv}>
                  <Upload className="w-4 h-4 mr-2" /> Import Approved
                </Button>
              </div>
              {importResult ? (
                <div className="md:col-span-4 rounded-lg border border-orange-200 bg-white p-3 text-sm">
                  <span className="font-semibold text-slate-900">Import Result:</span>{" "}
                  Imported <span className="font-semibold">{importResult.imported || 0}</span>, Updated{" "}
                  <span className="font-semibold">{importResult.updated || 0}</span>, Rejected{" "}
                  <span className="font-semibold">{importResult.rejected || 0}</span>
                  {(importResult.errors || []).length > 0 ? (
                    <div className="mt-2 max-h-32 overflow-auto text-xs text-red-700">
                      {(importResult.errors || []).map((err, idx) => (
                        <div key={idx}>Row {err.row}: {err.error}</div>
                      ))}
                    </div>
                  ) : null}
                </div>
              ) : null}
            </CardContent>
          </Card>

          <Card className="border-slate-200">
            <CardHeader><CardTitle className="text-base">{editingTemplateId ? "Edit SMS Template" : "Create SMS Template"}</CardTitle></CardHeader>
            <CardContent className="grid grid-cols-1 md:grid-cols-3 gap-3">
              <div className="space-y-2"><Label>Template Name</Label><Input value={templateForm.name} onChange={(e)=>setTemplateForm((p)=>({ ...p, name:e.target.value }))} /></div>
              <div className="space-y-2"><Label>Category</Label><Select value={templateForm.category} onValueChange={(v)=>setTemplateForm((p)=>({ ...p, category:v }))}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent><SelectItem value="service">Service</SelectItem><SelectItem value="otp">OTP</SelectItem><SelectItem value="transactional">Transactional</SelectItem><SelectItem value="promotional">Promotional</SelectItem></SelectContent></Select></div>
              <div className="space-y-2"><Label>Language</Label><Input value={templateForm.language} onChange={(e)=>setTemplateForm((p)=>({ ...p, language:e.target.value }))} /></div>
              <div className="space-y-2"><Label>Sender ID</Label><Input value={templateForm.smsSenderId} onChange={(e)=>setTemplateForm((p)=>({ ...p, smsSenderId:e.target.value.toUpperCase() }))} /></div>
              <div className="space-y-2"><Label>DLT Entity ID</Label><Input value={templateForm.dltEntityId} onChange={(e)=>setTemplateForm((p)=>({ ...p, dltEntityId:e.target.value }))} /></div>
              <div className="space-y-2"><Label>DLT Template ID</Label><Input value={templateForm.dltTemplateId} onChange={(e)=>setTemplateForm((p)=>({ ...p, dltTemplateId:e.target.value }))} /></div>
              <div className="space-y-2"><Label>Operator</Label><Select value={templateForm.smsOperator} onValueChange={(v)=>setTemplateForm((p)=>({ ...p, smsOperator:v }))}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent><SelectItem value="all">All</SelectItem><SelectItem value="jio">Jio</SelectItem><SelectItem value="vi">Vi</SelectItem><SelectItem value="airtel">Airtel</SelectItem></SelectContent></Select></div>
              <div className="space-y-2"><Label>Effective From</Label><Input type="datetime-local" value={templateForm.effectiveFromUtc} onChange={(e)=>setTemplateForm((p)=>({ ...p, effectiveFromUtc:e.target.value }))} /></div>
              <div className="space-y-2"><Label>Effective To</Label><Input type="datetime-local" value={templateForm.effectiveToUtc} onChange={(e)=>setTemplateForm((p)=>({ ...p, effectiveToUtc:e.target.value }))} /></div>
              <div className="space-y-2 md:col-span-3"><Label>Template Body</Label><Input value={templateForm.body} onChange={(e)=>setTemplateForm((p)=>({ ...p, body:e.target.value }))} placeholder="Example: Dear customer, your OTP is {{1}}" /></div>
              <div><Button className="bg-orange-500 hover:bg-orange-600 w-full" disabled={busy} onClick={saveTemplate}>{editingTemplateId?"Update Template":"Create Template"}</Button></div>
              {editingTemplateId?<div><Button variant="outline" className="w-full" onClick={()=>{setEditingTemplateId("");setTemplateForm(defaultTemplateForm);}}>Cancel Edit</Button></div>:null}
            </CardContent>
          </Card>

          {viewTemplate ? (
            <Card className="border-indigo-200 bg-indigo-50/40">
              <CardHeader><CardTitle className="text-base">Template Preview</CardTitle></CardHeader>
              <CardContent className="grid grid-cols-1 md:grid-cols-2 gap-3 text-sm">
                <div><span className="text-slate-500">Name:</span> <span className="font-medium">{viewTemplate.name}</span></div>
                <div><span className="text-slate-500">Template ID:</span> <span className="font-mono">{viewTemplate.dltTemplateId}</span></div>
                <div><span className="text-slate-500">Entity ID:</span> <span className="font-mono">{viewTemplate.dltEntityId}</span></div>
                <div><span className="text-slate-500">Sender:</span> <span className="font-mono">{viewTemplate.smsSenderId || "-"}</span></div>
                <div className="md:col-span-2"><span className="text-slate-500">Body:</span> <div className="mt-1 rounded border bg-white p-2">{viewTemplate.body}</div></div>
              </CardContent>
            </Card>
          ) : null}

          <Card className="border-slate-200"><CardHeader><CardTitle className="text-base">Template Registry</CardTitle></CardHeader><CardContent><Table><TableHeader><TableRow><TableHead>Name</TableHead><TableHead>Sender</TableHead><TableHead>DLT Template</TableHead><TableHead>Status</TableHead><TableHead>Operator</TableHead><TableHead className="text-right">Actions</TableHead></TableRow></TableHeader><TableBody>{templateRows.length===0?<TableRow><TableCell colSpan={6} className="text-slate-500">No templates created.</TableCell></TableRow>:null}{templateRows.map((r)=><TableRow key={r.id}><TableCell>{r.name}</TableCell><TableCell className="font-mono">{r.smsSenderId}</TableCell><TableCell className="font-mono">{r.dltTemplateId}</TableCell><TableCell>{statusBadge(r.lifecycleStatus || r.status)}</TableCell><TableCell>{r.smsOperator || "all"}</TableCell><TableCell className="text-right"><div className="inline-flex gap-2"><Button size="sm" variant="outline" onClick={()=>setViewTemplate(r)}><Eye className="w-4 h-4 mr-1" />View</Button><Button size="sm" variant="outline" onClick={()=>{setEditingTemplateId(r.id);setTemplateForm({name:r.name||"",category:r.category||"service",language:r.language||"en",body:r.body||"",smsSenderId:r.smsSenderId||"",dltEntityId:r.dltEntityId||"",dltTemplateId:r.dltTemplateId||"",smsOperator:r.smsOperator||"all",effectiveFromUtc:r.effectiveFromUtc?new Date(r.effectiveFromUtc).toISOString().slice(0,16):"",effectiveToUtc:r.effectiveToUtc?new Date(r.effectiveToUtc).toISOString().slice(0,16):""});}}>Edit</Button><Button size="sm" variant="ghost" className="text-red-600" onClick={()=>handleDeleteTemplate(r.id)}><Trash2 className="w-4 h-4 mr-1" />Delete</Button><Select onValueChange={async(v)=>{try{await setSmsTemplateStatus(r.id,{status:v,reason:v==="rejected"?"Rejected by operator": ""});toast.success("Status updated");await loadAll();}catch(e){toast.error(e?.message||"Failed to set status");}}}><SelectTrigger className="h-8 w-[130px]"><SelectValue placeholder="Update Status" /></SelectTrigger><SelectContent>{statusOptions.map((x)=><SelectItem key={x} value={x}>{x}</SelectItem>)}</SelectContent></Select></div></TableCell></TableRow>)}</TableBody></Table></CardContent></Card>
        </>
      )}

      {panel === "optouts" && (
        <>
          <Card className="border-slate-200"><CardHeader><CardTitle className="text-base">Opt-Out Management</CardTitle></CardHeader><CardContent className="grid md:grid-cols-3 gap-3"><div className="space-y-2"><Label>Phone</Label><Input placeholder="+919876543210" value={optOutPhone} onChange={(e)=>setOptOutPhone(e.target.value)} /></div><div className="space-y-2 md:col-span-2"><Label>Reason</Label><Input placeholder="Customer requested STOP" value={optOutReason} onChange={(e)=>setOptOutReason(e.target.value)} /></div><div><Button className="bg-orange-500 hover:bg-orange-600 w-full" disabled={busy} onClick={addOptOut}>Add Opt-Out</Button></div></CardContent></Card>
          <Card className="border-slate-200"><CardHeader><CardTitle className="text-base">Active Opt-Out List</CardTitle></CardHeader><CardContent><Table><TableHeader><TableRow><TableHead>Phone</TableHead><TableHead>Reason</TableHead><TableHead>Source</TableHead><TableHead>Since</TableHead><TableHead className="text-right">Actions</TableHead></TableRow></TableHeader><TableBody>{optOutRows.length===0?<TableRow><TableCell colSpan={5} className="text-slate-500">No opt-outs yet.</TableCell></TableRow>:null}{optOutRows.map((r)=><TableRow key={r.id}><TableCell className="font-mono">{r.phone}</TableCell><TableCell>{r.reason || "-"}</TableCell><TableCell>{r.source || "manual"}</TableCell><TableCell>{r.optedOutAtUtc ? new Date(r.optedOutAtUtc).toLocaleString() : "-"}</TableCell><TableCell className="text-right"><Button size="sm" variant="outline" onClick={async()=>{await removeSmsOptOut(r.id);toast.success("Opt-out removed");await loadAll();}}>Remove</Button></TableCell></TableRow>)}</TableBody></Table></CardContent></Card>
        </>
      )}

      {panel === "events" && (
        <Card className="border-slate-200"><CardHeader><CardTitle className="text-base">SMS Delivery Events (DLR)</CardTitle></CardHeader><CardContent><Table><TableHeader><TableRow><TableHead>Time</TableHead><TableHead>State</TableHead><TableHead>Provider Message ID</TableHead><TableHead>Phone</TableHead><TableHead>Delivery Message</TableHead></TableRow></TableHeader><TableBody>{events.length===0?<TableRow><TableCell colSpan={5} className="text-slate-500">No delivery events yet.</TableCell></TableRow>:null}{events.map((e)=><TableRow key={e.id}><TableCell>{e.createdAtUtc ? new Date(e.createdAtUtc).toLocaleString() : "-"}</TableCell><TableCell>{statusBadge(e.state)}</TableCell><TableCell className="font-mono text-xs">{e.providerMessageId || "-"}</TableCell><TableCell className="font-mono">{e.customerPhone || "-"}</TableCell><TableCell>{e.deliveryMessage || e.eventType || "-"}</TableCell></TableRow>)}</TableBody></Table></CardContent></Card>
      )}

      {panel === "billing" && (
        <Card className="border-slate-200">
          <CardHeader><CardTitle className="text-base">Per-Message Billing and Delivery</CardTitle></CardHeader>
          <CardContent>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Time</TableHead>
                  <TableHead>Recipient</TableHead>
                  <TableHead>State</TableHead>
                  <TableHead>Delivery Message</TableHead>
                  <TableHead>Charge</TableHead>
                  <TableHead>Provider Message ID</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {billingRows.length===0?<TableRow><TableCell colSpan={6} className="text-slate-500">No billing entries yet.</TableCell></TableRow>:null}
                {billingRows.map((row)=><TableRow key={row.id}>
                  <TableCell>{row.createdAtUtc ? new Date(row.createdAtUtc).toLocaleString() : "-"}</TableCell>
                  <TableCell className="font-mono">{row.recipient || "-"}</TableCell>
                  <TableCell>{statusBadge(row.deliveryState)}</TableCell>
                  <TableCell>{row.notes || row.deliveryMessage || "-"}</TableCell>
                  <TableCell className="font-medium">{row.currency || "INR"} {Number(row.totalAmount || 0).toFixed(2)}</TableCell>
                  <TableCell className="font-mono text-xs">{row.providerMessageId || "-"}</TableCell>
                </TableRow>)}
              </TableBody>
            </Table>
          </CardContent>
        </Card>
      )}

      {panel === "gateway" && (
        <Card className="border-slate-200">
          <CardHeader>
            <CardTitle className="text-base">Tenant Message Report (Tata Request/Response)</CardTitle>
          </CardHeader>
          <CardContent>
            {!gatewayReportEnabled ? (
              <div className="rounded-lg border border-amber-200 bg-amber-50 p-3 text-sm text-amber-900">
                This report is disabled for this tenant. Ask platform admin to enable it from Platform Owner Panel.
              </div>
            ) : (
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Time</TableHead>
                    <TableHead>Status</TableHead>
                    <TableHead>Recipient</TableHead>
                    <TableHead>Sender</TableHead>
                    <TableHead>Template</TableHead>
                    <TableHead>Message</TableHead>
                    <TableHead>Response</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {gatewayReportRows.length===0?<TableRow><TableCell colSpan={7} className="text-slate-500">No gateway logs yet.</TableCell></TableRow>:null}
                  {gatewayReportRows.map((row)=><TableRow key={row.id}>
                    <TableCell>{row.createdAtUtc ? new Date(row.createdAtUtc).toLocaleString() : "-"}</TableCell>
                    <TableCell>{row.isSuccess ? <Badge className="bg-green-100 text-green-700 hover:bg-green-100">Success</Badge> : <Badge className="bg-red-100 text-red-700 hover:bg-red-100">Failed</Badge>}</TableCell>
                    <TableCell className="font-mono">{row.recipient || "-"}</TableCell>
                    <TableCell className="font-mono">{row.sender || "-"}</TableCell>
                    <TableCell className="font-mono text-xs">{row.templateId || "-"}</TableCell>
                    <TableCell className="max-w-[320px] truncate" title={row.decodedMessage || row.requestUrlMasked || ""}>{row.decodedMessage || "-"}</TableCell>
                    <TableCell className="max-w-[320px] truncate text-xs" title={row.responseBody || row.error || ""}>{row.responseBody || row.error || "-"}</TableCell>
                  </TableRow>)}
                </TableBody>
              </Table>
            )}
          </CardContent>
        </Card>
      )}
    </div>
  );
};

export default SmsSetupPage;

