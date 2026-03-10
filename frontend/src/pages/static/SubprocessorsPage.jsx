import { Link } from "react-router-dom";
import { ArrowLeft } from "lucide-react";
import StaticShell from "@/components/marketing/StaticShell";

export default function SubprocessorsPage() {
  return (
    <StaticShell>
      <div className="max-w-4xl mx-auto px-6 py-10 space-y-6">
        <div className="flex items-center gap-3 text-slate-500 text-sm">
          <Link to="/" className="flex items-center gap-2 text-orange-500 hover:text-orange-600">
            <ArrowLeft className="w-4 h-4" /> Back to home
          </Link>
          <span>·</span>
          <span>Subprocessors</span>
        </div>
        <h1 className="text-3xl font-bold text-slate-900">Subprocessor List</h1>
        <p className="text-sm text-slate-500">Last Updated: March 10, 2026</p>
        <p className="text-slate-700">
          This page lists the subprocessors used by Textzy, operated by Moneyart Private Limited, to provide messaging and infrastructure services.
        </p>

        <h2 className="text-xl font-semibold text-slate-900">1. Infrastructure Providers</h2>
        <p className="text-slate-700">Examples may include:</p>
        <ul className="list-disc pl-5 space-y-1 text-slate-700">
          <li>Cloud Infrastructure Provider — Hosting platform services</li>
          <li>CDN / Network Services — Content delivery and network protection</li>
          <li>Monitoring Services — System monitoring and reliability</li>
        </ul>

        <h2 className="text-xl font-semibold text-slate-900">2. Messaging Network Providers</h2>
        <ul className="list-disc pl-5 space-y-1 text-slate-700">
          <li>SMS Gateway Providers — SMS message routing and delivery</li>
          <li>Telecom Operators — Message termination to mobile networks</li>
          <li>WhatsApp Business Platform Providers — WhatsApp messaging infrastructure</li>
        </ul>

        <h2 className="text-xl font-semibold text-slate-900">3. Analytics and Monitoring Providers</h2>
        <ul className="list-disc pl-5 space-y-1 text-slate-700">
          <li>Analytics Platforms — Usage insights and service improvements</li>
          <li>Logging Services — System diagnostics and troubleshooting</li>
        </ul>

        <h2 className="text-xl font-semibold text-slate-900">4. Data Protection</h2>
        <p className="text-slate-700">
          All subprocessors are evaluated to ensure they maintain appropriate security and privacy standards. Where applicable, contracts include
          data protection obligations and subprocessors process data only as required for service delivery.
        </p>

        <h2 className="text-xl font-semibold text-slate-900">5. Subprocessor Updates</h2>
        <p className="text-slate-700">
          Textzy may update this list from time to time as infrastructure providers change. Customers may request notification of new subprocessors by
          contacting privacy@textzy.in.
        </p>

        <h2 className="text-xl font-semibold text-slate-900">6. Contact</h2>
        <p className="text-slate-700">Moneyart Private Limited</p>
        <p className="text-slate-700">Email: privacy@textzy.in</p>
        <p className="text-slate-700">Website: https://textzy.in</p>
      </div>
    </StaticShell>
  );
}
