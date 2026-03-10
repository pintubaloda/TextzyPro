import { Link } from "react-router-dom";
import { ArrowLeft } from "lucide-react";
import StaticShell from "@/components/marketing/StaticShell";
import { useBranding } from "@/hooks/useBranding";

export default function RefundPage() {
  const { brand } = useBranding();
  return (
    <StaticShell>
      <div className="max-w-4xl mx-auto px-6 py-10 space-y-6">
        <div className="flex items-center gap-3 text-slate-500 text-sm">
          <Link to="/" className="flex items-center gap-2 text-orange-500 hover:text-orange-600">
            <ArrowLeft className="w-4 h-4" /> Back to home
          </Link>
          <span>·</span>
          <span>Refund Policy</span>
        </div>
        <h1 className="text-3xl font-bold text-slate-900">Refund Policy</h1>
        <ul className="list-disc pl-5 space-y-2 text-slate-700 leading-relaxed">
          <li>Prepaid credits are refundable on unused balance within 7 business days.</li>
          <li>Channel fees already consumed (WhatsApp template sends, SMS DLT delivery) are non-refundable.</li>
          <li>Email your Tenant ID and invoice ID to {brand.email} to initiate a refund.</li>
          <li>Approved refunds are processed to the original payment method.</li>
        </ul>
      </div>
    </StaticShell>
  );
}
