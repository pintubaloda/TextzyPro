import { Link } from "react-router-dom";
import { ArrowLeft, Check } from "lucide-react";
import StaticShell from "@/components/marketing/StaticShell";
import { useBranding } from "@/hooks/useBranding";

export default function AboutPage() {
  const { brand } = useBranding();
  return (
    <StaticShell>
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-12 space-y-10">
        <div className="flex items-center gap-3 text-slate-500 text-sm">
          <Link to="/" className="flex items-center gap-2 text-orange-500 hover:text-orange-600">
            <ArrowLeft className="w-4 h-4" /> Back to home
          </Link>
          <span>·</span>
          <span>About {brand.name}</span>
        </div>

        <h1 className="text-4xl font-bold text-slate-900">About Textzy</h1>
        <p className="text-lg text-slate-600 leading-relaxed max-w-4xl">
          Textzy is a modern business messaging platform designed to help companies communicate with their customers through WhatsApp Business API
          and SMS infrastructure. Textzy is a brand of Moneyart Private Limited, focused on delivering reliable, compliant, and secure
          communication solutions for businesses of all sizes.
        </p>
        <p className="text-slate-600 leading-relaxed max-w-4xl">
          In today’s digital economy, businesses rely heavily on messaging channels to deliver notifications, marketing updates, customer support
          responses, and transactional alerts. However, managing compliance, delivery reliability, and real-time engagement across messaging channels
          can be complex.
        </p>
        <p className="text-slate-600 leading-relaxed max-w-4xl">
          Textzy simplifies this process by providing a unified communication platform built for WhatsApp onboarding, DLT-compliant SMS delivery,
          and real-time messaging infrastructure.
        </p>

        <div className="grid md:grid-cols-2 gap-6">
          <div className="p-5 rounded-xl border border-slate-200 bg-slate-50">
            <h3 className="font-semibold text-slate-900 mb-2">Our Mission</h3>
            <p className="text-slate-600 leading-relaxed">
              Our mission is to empower businesses with a secure, scalable, and compliant customer communication platform that ensures reliable
              message delivery and seamless engagement across messaging channels.
            </p>
            <p className="text-slate-600 leading-relaxed mt-3">
              We believe that business communication should be fast, secure, transparent, and compliant with telecom regulations while remaining
              easy for developers and businesses to integrate.
            </p>
          </div>
          <div className="p-5 rounded-xl border border-slate-200 bg-slate-50">
            <h3 className="font-semibold text-slate-900 mb-3">What Textzy Solves</h3>
            <ul className="space-y-3 text-slate-700">
              <li className="flex gap-2">
                <Check className="w-4 h-4 text-orange-500 mt-1" />
                <span><strong>WhatsApp Business API Onboarding</strong> — We manage template governance, compliance checks, and platform integration so businesses can send verified WhatsApp messages quickly and reliably.</span>
              </li>
              <li className="flex gap-2">
                <Check className="w-4 h-4 text-orange-500 mt-1" />
                <span><strong>DLT-Compliant SMS Gateway</strong> — DLT-ready infrastructure with intelligent throttling, delivery monitoring, and detailed analytics for regulatory compliance.</span>
              </li>
              <li className="flex gap-2">
                <Check className="w-4 h-4 text-orange-500 mt-1" />
                <span><strong>Real-Time Messaging Inbox</strong> — SignalR + queue-backed infrastructure keeps messages flowing reliably, even under high traffic.</span>
              </li>
              <li className="flex gap-2">
                <Check className="w-4 h-4 text-orange-500 mt-1" />
                <span><strong>Security-First Platform</strong> — CSRF protection, 2FA, IP allow-listing, and audit trails ensure safe, compliant operations.</span>
              </li>
            </ul>
          </div>
        </div>

        <div className="grid md:grid-cols-2 gap-6">
          <div className="p-5 rounded-xl border border-slate-200 bg-slate-50">
            <h3 className="font-semibold text-slate-900 mb-3">Built for Modern Businesses</h3>
            <ul className="space-y-2 text-slate-700">
              <li className="flex gap-2"><Check className="w-4 h-4 text-orange-500 mt-1" /> Customer notifications</li>
              <li className="flex gap-2"><Check className="w-4 h-4 text-orange-500 mt-1" /> OTP authentication</li>
              <li className="flex gap-2"><Check className="w-4 h-4 text-orange-500 mt-1" /> Marketing campaigns</li>
              <li className="flex gap-2"><Check className="w-4 h-4 text-orange-500 mt-1" /> Transactional alerts</li>
              <li className="flex gap-2"><Check className="w-4 h-4 text-orange-500 mt-1" /> Customer support communication</li>
            </ul>
          </div>
          <div className="p-5 rounded-xl border border-slate-200 bg-slate-50">
            <h3 className="font-semibold text-slate-900 mb-3">About Moneyart Private Limited</h3>
            <p className="text-slate-600 leading-relaxed">
              Textzy is a product of Moneyart Private Limited, a technology company focused on building scalable digital infrastructure and
              communication solutions for modern businesses.
            </p>
            <div className="mt-4 text-slate-700 text-sm">
              Contact: <a className="text-orange-600" href={`mailto:${brand.email}`}>{brand.email}</a> ·{" "}
              <a className="text-orange-600" href={`tel:${brand.phone?.replace(/\s+/g, "")}`}>{brand.phone}</a>
              <div className="mt-2">Address: {brand.address}</div>
            </div>
          </div>
        </div>
      </div>
    </StaticShell>
  );
}
