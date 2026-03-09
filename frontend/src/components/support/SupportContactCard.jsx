import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { LifeBuoy, Mail, MapPin, MessageCircle, Ticket } from "lucide-react";

const sanitizePhone = (value) => String(value || "").replace(/[^\d]/g, "");

export default function SupportContactCard({
  support,
  project,
  onCreateTicket,
  onOpenSupportDesk,
  compact = false,
}) {
  const supportPhone = String(support?.supportPhone || "").trim();
  const supportEmail = String(support?.supportEmail || "").trim();
  const whatsappUrl = supportPhone ? `https://wa.me/${sanitizePhone(supportPhone)}` : "";
  const mailtoUrl = supportEmail ? `mailto:${supportEmail}` : "";

  return (
    <Card className="border-slate-200 shadow-sm">
      <CardHeader>
        <div className="flex items-start justify-between gap-3">
          <div>
            <CardTitle className="flex items-center gap-2">
              <LifeBuoy className="h-5 w-5 text-orange-500" />
              Platform Support
            </CardTitle>
            <CardDescription>Reach Textzy support or open a tracked service ticket.</CardDescription>
          </div>
          <Badge className="bg-orange-100 text-orange-700 hover:bg-orange-100">Support Desk</Badge>
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="rounded-2xl border border-slate-200 bg-slate-50/80 p-4">
          <p className="text-base font-semibold text-slate-950">{support?.legalName || support?.platformName || "Textzy"}</p>
          {support?.gstin ? <p className="mt-2 text-sm text-slate-600">GSTIN: {support.gstin}</p> : null}
          {support?.address ? (
            <div className="mt-3 flex items-start gap-2 text-sm text-slate-600">
              <MapPin className="mt-0.5 h-4 w-4 flex-shrink-0 text-slate-400" />
              <span className="whitespace-pre-line">{support.address}</span>
            </div>
          ) : null}
          {project?.projectName ? (
            <div className="mt-4 rounded-xl border border-orange-100 bg-white px-3 py-2 text-sm text-slate-600">
              Active project: <span className="font-medium text-slate-900">{project.projectName}</span>
            </div>
          ) : null}
        </div>

        <div className={`grid gap-3 ${compact ? "" : "sm:grid-cols-2"}`}>
          <div className="rounded-2xl border border-slate-200 bg-white p-4">
            <p className="text-xs font-semibold uppercase tracking-[0.16em] text-slate-500">Support Email</p>
            <p className="mt-2 text-sm font-medium text-slate-900">{supportEmail || "Not configured"}</p>
          </div>
          <div className="rounded-2xl border border-slate-200 bg-white p-4">
            <p className="text-xs font-semibold uppercase tracking-[0.16em] text-slate-500">WhatsApp Support</p>
            <p className="mt-2 text-sm font-medium text-slate-900">{supportPhone || "Not configured"}</p>
          </div>
        </div>

        <div className="flex flex-wrap gap-2">
          <Button className="bg-orange-500 text-white hover:bg-orange-600" onClick={onCreateTicket}>
            <Ticket className="mr-2 h-4 w-4" />
            Create Ticket
          </Button>
          <Button variant="outline" onClick={onOpenSupportDesk}>
            <MessageCircle className="mr-2 h-4 w-4" />
            Open Support Desk
          </Button>
          <Button variant="outline" disabled={!supportEmail} asChild={!!mailtoUrl}>
            {mailtoUrl ? (
              <a href={mailtoUrl}>
                <Mail className="mr-2 h-4 w-4" />
                Email Support
              </a>
            ) : (
              <span>
                <Mail className="mr-2 h-4 w-4" />
                Email Support
              </span>
            )}
          </Button>
          <Button variant="outline" disabled={!whatsappUrl} asChild={!!whatsappUrl}>
            {whatsappUrl ? (
              <a href={whatsappUrl} target="_blank" rel="noreferrer">
                <MessageCircle className="mr-2 h-4 w-4" />
                WhatsApp Support
              </a>
            ) : (
              <span>
                <MessageCircle className="mr-2 h-4 w-4" />
                WhatsApp Support
              </span>
            )}
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}
