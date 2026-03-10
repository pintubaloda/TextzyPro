import { Link } from "react-router-dom";
import { ArrowLeft } from "lucide-react";
import StaticShell from "@/components/marketing/StaticShell";

export default function AupPage() {
  return (
    <StaticShell>
      <div className="max-w-4xl mx-auto px-6 py-10 space-y-6">
        <div className="flex items-center gap-3 text-slate-500 text-sm">
          <Link to="/" className="flex items-center gap-2 text-orange-500 hover:text-orange-600">
            <ArrowLeft className="w-4 h-4" /> Back to home
          </Link>
          <span>·</span>
          <span>Acceptable Use</span>
        </div>
        <h1 className="text-3xl font-bold text-slate-900">Acceptable Use Policy (AUP)</h1>
        <p className="text-sm text-slate-500">Last Updated: March 10, 2026</p>
        <p className="text-slate-700">
          This Acceptable Use Policy defines permitted and prohibited uses of Textzy, a messaging platform operated by Moneyart Private Limited.
        </p>

        <h2 className="text-xl font-semibold text-slate-900">1. Purpose</h2>
        <ul className="list-disc pl-5 space-y-1 text-slate-700">
          <li>Prevent spam and abusive messaging</li>
          <li>Ensure compliance with messaging regulations</li>
          <li>Protect recipients from unwanted communication</li>
          <li>Maintain reliability and integrity of the Textzy platform</li>
        </ul>

        <h2 className="text-xl font-semibold text-slate-900">2. Prohibited Messaging Activities</h2>
        <ul className="list-disc pl-5 space-y-1 text-slate-700">
          <li>Spam or unsolicited messages</li>
          <li>Fraud or deceptive practices (phishing, impersonation, scams)</li>
          <li>Harassment or abuse</li>
          <li>Illegal activities</li>
          <li>Malware distribution</li>
        </ul>

        <h2 className="text-xl font-semibold text-slate-900">3. Restricted Industries</h2>
        <p className="text-slate-700">
          Certain industries may be restricted or prohibited depending on regulatory or messaging platform policies, including illegal products or services,
          unauthorized financial schemes, gambling where restricted, adult content services, weapons, or explosives.
        </p>

        <h2 className="text-xl font-semibold text-slate-900">4. Messaging Consent Requirements</h2>
        <p className="text-slate-700">
          Customers must obtain clear and verifiable user consent before sending messages. Consent must include identification of the sender, message purpose,
          and ability to opt out. Customers must maintain records of consent when required by law.
        </p>

        <h2 className="text-xl font-semibold text-slate-900">5. Rate Limits and Platform Protection</h2>
        <p className="text-slate-700">
          Textzy may enforce rate limits, throttle throughput, and temporarily block high-risk traffic to protect infrastructure and prevent abuse.
        </p>

        <h2 className="text-xl font-semibold text-slate-900">6. Enforcement Actions</h2>
        <ul className="list-disc pl-5 space-y-1 text-slate-700">
          <li>Warning notices</li>
          <li>Message blocking</li>
          <li>Temporary suspension of services</li>
          <li>Permanent account termination</li>
        </ul>

        <h2 className="text-xl font-semibold text-slate-900">7. Reporting Violations</h2>
        <p className="text-slate-700">Email: legal@textzy.in</p>

        <h2 className="text-xl font-semibold text-slate-900">8. Policy Updates</h2>
        <p className="text-slate-700">Textzy may update this Acceptable Use Policy periodically.</p>
      </div>
    </StaticShell>
  );
}
