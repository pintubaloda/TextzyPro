import { BookOpenText, Copy, ExternalLink, Globe2, MessageSquareText, ShieldCheck } from "lucide-react";
import { toast } from "sonner";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";

const DOCS = {
  sms: {
    title: "Textzy API Documentation",
    description: "Single professional API document for SMS, WhatsApp, and KYC integrations with production-ready examples and implementation notes.",
    html: "/docs/index.html#sms-send",
    chip: "SMS",
    tone: "from-orange-50 via-white to-amber-50",
    icon: MessageSquareText,
  },
  whatsapp: {
    title: "Textzy API Documentation",
    description: "Single professional API document for SMS, WhatsApp, and KYC integrations with production-ready examples and implementation notes.",
    html: "/docs/index.html#whatsapp-send",
    chip: "WhatsApp",
    tone: "from-cyan-50 via-white to-emerald-50",
    icon: Globe2,
  },
  kyc: {
    title: "Textzy API Documentation",
    description: "Single professional API document for SMS, WhatsApp, and KYC integrations with production-ready examples and implementation notes.",
    html: "/docs/index.html#kyc-digilocker",
    chip: "KYC",
    tone: "from-emerald-50 via-white to-lime-50",
    icon: ShieldCheck,
  },
};

function getDoc(type) {
  return DOCS[type] || DOCS.sms;
}

async function copyText(text, label) {
  await navigator.clipboard.writeText(text);
  toast.success(`${label} copied`);
}

export default function ApiDocsViewer({ open, onOpenChange, type, onTypeChange }) {
  const docMeta = getDoc(type);
  const ActiveIcon = docMeta.icon;

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-[96vw] overflow-hidden border-slate-200 p-0 sm:max-w-[96vw]">
        <DialogHeader className={`border-b border-slate-200 bg-gradient-to-r ${docMeta.tone} px-6 py-5`}>
          <div className="flex flex-col gap-4 xl:flex-row xl:items-start xl:justify-between">
            <div className="space-y-2">
              <div className="flex items-center gap-2">
                <Badge className="rounded-full bg-slate-900 px-3 py-1 text-white hover:bg-slate-900">
                  <ActiveIcon className="mr-1.5 h-3.5 w-3.5" />
                  {docMeta.chip}
                </Badge>
                <Badge variant="outline" className="rounded-full border-slate-300 bg-white/80 text-slate-700">
                  Single final documentation
                </Badge>
              </div>
              <DialogTitle className="text-2xl font-bold text-slate-950">{docMeta.title}</DialogTitle>
              <DialogDescription className="max-w-3xl text-sm text-slate-600">
                {docMeta.description}
              </DialogDescription>
              <div className="grid gap-3 pt-2 sm:grid-cols-3">
                <div className="rounded-2xl border border-white/70 bg-white/75 px-4 py-3">
                  <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-slate-500">Document Type</p>
                  <p className="mt-1 text-lg font-bold text-slate-950">One Final HTML</p>
                </div>
                <div className="rounded-2xl border border-white/70 bg-white/75 px-4 py-3">
                  <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-slate-500">Use Case</p>
                  <p className="mt-1 text-lg font-bold text-slate-950">Partner Integrations</p>
                </div>
                <div className="rounded-2xl border border-white/70 bg-white/75 px-4 py-3">
                  <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-slate-500">Includes</p>
                  <p className="mt-1 text-lg font-bold text-slate-950">Examples + Quick Start</p>
                </div>
              </div>
            </div>
            <div className="flex flex-wrap gap-2">
              <Button variant={type === "sms" ? "default" : "outline"} className={type === "sms" ? "bg-orange-500 hover:bg-orange-600" : ""} onClick={() => onTypeChange("sms")}>
                <MessageSquareText className="mr-2 h-4 w-4" />SMS API
              </Button>
              <Button variant={type === "whatsapp" ? "default" : "outline"} className={type === "whatsapp" ? "bg-orange-500 hover:bg-orange-600" : ""} onClick={() => onTypeChange("whatsapp")}>
                <Globe2 className="mr-2 h-4 w-4" />WhatsApp API
              </Button>
              <Button variant={type === "kyc" ? "default" : "outline"} className={type === "kyc" ? "bg-orange-500 hover:bg-orange-600" : ""} onClick={() => onTypeChange("kyc")}>
                <BookOpenText className="mr-2 h-4 w-4" />KYC API
              </Button>
              <Button variant="outline" onClick={() => copyText(`${window.location.origin}${docMeta.html}`, "Viewer link")}>
                <Copy className="mr-2 h-4 w-4" />Copy Link
              </Button>
              <Button variant="outline" onClick={() => window.open(docMeta.html, "_blank", "noopener,noreferrer")}>
                <ExternalLink className="mr-2 h-4 w-4" />Open Full Document
              </Button>
            </div>
          </div>
        </DialogHeader>
        <div className="h-[82vh] bg-slate-100 p-3">
          <div className="h-full overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm">
            <iframe title="Textzy API Documentation" src={docMeta.html} className="h-full w-full border-0" />
          </div>
        </div>
      </DialogContent>
    </Dialog>
  );
}
