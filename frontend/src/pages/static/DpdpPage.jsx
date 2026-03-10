import { Link } from "react-router-dom";
import { ArrowLeft } from "lucide-react";
import StaticShell from "@/components/marketing/StaticShell";

export default function DpdpPage() {
  return (
    <StaticShell>
      <div className="max-w-4xl mx-auto px-6 py-10 space-y-6">
        <div className="flex items-center gap-3 text-slate-500 text-sm">
          <Link to="/" className="flex items-center gap-2 text-orange-500 hover:text-orange-600">
            <ArrowLeft className="w-4 h-4" /> Back to home
          </Link>
          <span>·</span>
          <span>DPDP Act</span>
        </div>
        <h1 className="text-3xl font-bold text-slate-900">DPDP Act (India) Compliant Privacy Policy</h1>
        <p className="text-sm text-slate-500">Last Updated: March 10, 2026</p>
        <p className="text-slate-700">
          This Privacy Policy explains how Textzy, operated by Moneyart Private Limited, collects, processes, and protects personal data in
          accordance with the Digital Personal Data Protection Act, 2023 (DPDP Act).
        </p>

        <div className="space-y-4">
          <h2 className="text-xl font-semibold text-slate-900">1. Personal Data We Collect</h2>
          <p className="text-slate-700 font-medium">Account Information</p>
          <ul className="list-disc pl-5 space-y-1 text-slate-700">
            <li>Name</li>
            <li>Email address</li>
            <li>Phone number</li>
            <li>Company information</li>
          </ul>
          <p className="text-slate-700 font-medium">Technical Information</p>
          <ul className="list-disc pl-5 space-y-1 text-slate-700">
            <li>IP address</li>
            <li>Device information</li>
            <li>API usage logs</li>
          </ul>
          <p className="text-slate-700 font-medium">Messaging Data</p>
          <ul className="list-disc pl-5 space-y-1 text-slate-700">
            <li>Sender and recipient numbers</li>
            <li>Message metadata</li>
            <li>Delivery reports</li>
            <li>Template data</li>
          </ul>
        </div>

        <div className="space-y-4">
          <h2 className="text-xl font-semibold text-slate-900">2. Purpose of Data Processing</h2>
          <ul className="list-disc pl-5 space-y-1 text-slate-700">
            <li>Provide messaging services</li>
            <li>Enable WhatsApp onboarding</li>
            <li>Deliver SMS via telecom networks</li>
            <li>Improve platform performance</li>
            <li>Prevent fraud and abuse</li>
            <li>Comply with legal obligations</li>
          </ul>
        </div>

        <div className="space-y-4">
          <h2 className="text-xl font-semibold text-slate-900">3. Consent</h2>
          <p className="text-slate-700">
            Under the DPDP Act, personal data is processed only for lawful purposes. By using Textzy services, you consent to processing of necessary
            account information and messaging metadata required to deliver messages. Users are responsible for obtaining customer consent before sending messages.
          </p>
        </div>

        <div className="space-y-4">
          <h2 className="text-xl font-semibold text-slate-900">4. Data Fiduciary</h2>
          <p className="text-slate-700">
            Under the DPDP Act, Moneyart Private Limited acts as the Data Fiduciary for the personal data processed through Textzy.
          </p>
        </div>

        <div className="space-y-4">
          <h2 className="text-xl font-semibold text-slate-900">5. Data Retention</h2>
          <p className="text-slate-700">
            Personal data is retained only as long as necessary for service delivery, regulatory compliance, and platform security.
          </p>
        </div>

        <div className="space-y-4">
          <h2 className="text-xl font-semibold text-slate-900">6. User Rights (DPDP Act)</h2>
          <ul className="list-disc pl-5 space-y-1 text-slate-700">
            <li>Access their personal data</li>
            <li>Request correction of inaccurate data</li>
            <li>Request deletion of personal data</li>
            <li>Withdraw consent where applicable</li>
            <li>Nominate a representative to exercise rights</li>
          </ul>
          <p className="text-slate-700">Requests can be submitted via privacy@textzy.in.</p>
        </div>

        <div className="space-y-4">
          <h2 className="text-xl font-semibold text-slate-900">7. Security Safeguards</h2>
          <ul className="list-disc pl-5 space-y-1 text-slate-700">
            <li>Encryption</li>
            <li>Access control</li>
            <li>Audit logs</li>
            <li>Two-factor authentication</li>
            <li>Infrastructure monitoring</li>
          </ul>
        </div>

        <div className="space-y-4">
          <h2 className="text-xl font-semibold text-slate-900">8. Grievance Officer</h2>
          <p className="text-slate-700">Grievance Officer, Moneyart Private Limited</p>
          <p className="text-slate-700">Email: legal@textzy.in</p>
          <p className="text-slate-700">Complaints will be acknowledged and addressed within the timeframe required by law.</p>
        </div>
      </div>
    </StaticShell>
  );
}
