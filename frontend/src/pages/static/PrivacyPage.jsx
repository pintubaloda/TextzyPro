import { Link } from "react-router-dom";
import { ArrowLeft } from "lucide-react";
import StaticShell from "@/components/marketing/StaticShell";
import { useBranding } from "@/hooks/useBranding";

export default function PrivacyPage() {
  const { brand } = useBranding();
  return (
    <StaticShell>
      <div className="max-w-4xl mx-auto px-6 py-10 space-y-6">
        <div className="flex items-center gap-3 text-slate-500 text-sm">
          <Link to="/" className="flex items-center gap-2 text-orange-500 hover:text-orange-600">
            <ArrowLeft className="w-4 h-4" /> Back to home
          </Link>
          <span>·</span>
          <span>Privacy Policy</span>
        </div>
        <h1 className="text-3xl font-bold text-slate-900">Privacy Policy</h1>
        <p className="text-slate-600 leading-relaxed">
          We collect only what we need to deliver {brand.name} messaging services (WhatsApp Business API and SMS). Data is encrypted in transit and at rest.
        </p>
        <ul className="list-disc pl-5 space-y-2 text-slate-700">
          <li>Authentication data (email, session tokens, CSRF) for secure access.</li>
          <li>Message metadata for delivery, retries, and compliance logs.</li>
          <li>No resale of personal data; sharing only with processors required for messaging (WABA/SMS providers).</li>
          <li>Access is role-based; actions are audited for platform admins.</li>
        </ul>
        <p className="text-slate-700">For deletion/exports contact: {brand.email}</p>
      </div>
    </StaticShell>
  );
}
