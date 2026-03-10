import { Link } from "react-router-dom";
import { ArrowLeft, Check } from "lucide-react";

const brandFromConfig = () => {
  const cfg = (typeof window !== "undefined" ? window._APP_CONFIG_ : {}) || {};
  return {
    name: cfg.BRAND_NAME || "Textzy",
    companyLine: cfg.BRAND_COMPANY_LINE || "Textzy is a brand of Moneyart Private Limited.",
    tagline: cfg.BRAND_TAGLINE || "Business inbox for WhatsApp & SMS.",
    address: cfg.BRAND_ADDRESS || "Mumbai, India",
    phone: cfg.BRAND_PHONE || "+91 22 1234 5678",
    email: cfg.BRAND_EMAIL || "hello@textzy.in",
  };
};

export default function AboutPage() {
  const brand = brandFromConfig();
  return (
    <div className="min-h-screen bg-white">
      <div className="max-w-5xl mx-auto px-6 py-10 space-y-8">
        <div className="flex items-center gap-3 text-slate-500 text-sm">
          <Link to="/" className="flex items-center gap-2 text-orange-500 hover:text-orange-600">
            <ArrowLeft className="w-4 h-4" /> Back to home
          </Link>
          <span>·</span>
          <span>About {brand.name}</span>
        </div>
        <h1 className="text-4xl font-bold text-slate-900">About {brand.name}</h1>
        <p className="text-lg text-slate-600 leading-relaxed max-w-3xl">
          {brand.name} is a brand of Moneyart Private Limited. We help teams deliver reliable customer conversations
          over WhatsApp Business API and DLT-compliant SMS, combining real-time inbox, automation, analytics, and
          platform-grade security.
        </p>
        <div className="grid md:grid-cols-2 gap-6">
          <div className="p-5 rounded-xl border border-slate-200 bg-slate-50">
            <h3 className="font-semibold text-slate-900 mb-3">What we solve</h3>
            <ul className="space-y-2 text-slate-700">
              <li className="flex gap-2"><Check className="w-4 h-4 text-orange-500 mt-1" /> Official WhatsApp onboarding and template governance.</li>
              <li className="flex gap-2"><Check className="w-4 h-4 text-orange-500 mt-1" /> DLT-ready SMS delivery with throttling and analytics.</li>
              <li className="flex gap-2"><Check className="w-4 h-4 text-orange-500 mt-1" /> Real-time SignalR inbox with queue-backed reliability.</li>
              <li className="flex gap-2"><Check className="w-4 h-4 text-orange-500 mt-1" /> Security-first: CSRF/2FA/IP allowlists and audit trails.</li>
            </ul>
          </div>
          <div className="p-5 rounded-xl border border-slate-200 bg-slate-50">
            <h3 className="font-semibold text-slate-900 mb-3">At a glance</h3>
            <ul className="space-y-2 text-slate-700">
              <li className="flex gap-2"><Check className="w-4 h-4 text-orange-500 mt-1" /> {brand.companyLine}</li>
              <li className="flex gap-2"><Check className="w-4 h-4 text-orange-500 mt-1" /> {brand.tagline}</li>
              <li className="flex gap-2"><Check className="w-4 h-4 text-orange-500 mt-1" /> Contact: {brand.email}, {brand.phone}</li>
              <li className="flex gap-2"><Check className="w-4 h-4 text-orange-500 mt-1" /> Address: {brand.address}</li>
            </ul>
          </div>
        </div>
      </div>
    </div>
  );
}
