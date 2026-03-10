import { Link } from "react-router-dom";
import { ArrowLeft } from "lucide-react";
import StaticShell from "@/components/marketing/StaticShell";

export default function TrustCenterPage() {
  return (
    <StaticShell>
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-10 space-y-6">
        <div className="flex items-center gap-3 text-slate-500 text-sm">
          <Link to="/" className="flex items-center gap-2 text-orange-500 hover:text-orange-600">
            <ArrowLeft className="w-4 h-4" /> Back to home
          </Link>
          <span>·</span>
          <span>Trust Center</span>
        </div>
        <h1 className="text-3xl font-bold text-slate-900">Textzy Trust Center</h1>
        <p className="text-sm text-slate-500">Last Updated: March 10, 2026</p>
        <p className="text-slate-700">
          At Textzy, trust, security, and compliance are fundamental to how we design and operate our messaging platform. Operated by Moneyart Private Limited,
          Textzy is built to help businesses communicate with customers through reliable, compliant, and secure messaging infrastructure.
        </p>
        <p className="text-slate-700">
          This Trust Center provides an overview of the policies, safeguards, and compliance measures implemented to protect our customers and their data.
        </p>

        <h2 className="text-xl font-semibold text-slate-900">Security</h2>
        <ul className="list-disc pl-5 space-y-1 text-slate-700">
          <li>Encrypted communication using HTTPS/TLS</li>
          <li>Two-Factor Authentication (2FA) for administrative access</li>
          <li>IP allowlisting for sensitive operations</li>
          <li>Role-based access control</li>
          <li>Audit logging and monitoring</li>
          <li>Protection against CSRF and common web vulnerabilities</li>
          <li>Continuous infrastructure monitoring</li>
        </ul>

        <h2 className="text-xl font-semibold text-slate-900">Privacy and Data Protection</h2>
        <ul className="list-disc pl-5 space-y-1 text-slate-700">
          <li>Data minimization practices</li>
          <li>Secure processing of messaging metadata</li>
          <li>Protection of user account information</li>
          <li>Controlled access to operational data</li>
          <li>Secure infrastructure hosting</li>
        </ul>
        <p className="text-slate-700">Textzy processes data only as necessary to deliver messaging services. For more information, review our Privacy Policy.</p>

        <h2 className="text-xl font-semibold text-slate-900">Regulatory Compliance</h2>
        <ul className="list-disc pl-5 space-y-1 text-slate-700">
          <li>India Digital Personal Data Protection Act (DPDP Act, 2023)</li>
          <li>TRAI telecom messaging regulations</li>
          <li>DLT (Distributed Ledger Technology) SMS compliance</li>
          <li>WhatsApp Business messaging policies</li>
          <li>GDPR data protection principles for international users</li>
        </ul>
        <p className="text-slate-700">
          Customers remain responsible for ensuring that their messaging campaigns comply with applicable laws and consent requirements.
        </p>

        <h2 className="text-xl font-semibold text-slate-900">Messaging Compliance</h2>
        <ul className="list-disc pl-5 space-y-1 text-slate-700">
          <li>Consent-based messaging requirements</li>
          <li>Template approval for messaging</li>
          <li>Messaging rate limits and throttling</li>
          <li>Monitoring for suspicious messaging activity</li>
          <li>Enforcement actions for policy violations</li>
        </ul>
        <p className="text-slate-700">Customers must obtain user opt-in consent before sending messages.</p>

        <h2 className="text-xl font-semibold text-slate-900">Platform Reliability</h2>
        <ul className="list-disc pl-5 space-y-1 text-slate-700">
          <li>Queue-backed messaging architecture</li>
          <li>Real-time messaging systems powered by SignalR</li>
          <li>Delivery tracking and analytics</li>
          <li>Scalable messaging throughput controls</li>
        </ul>
        <p className="text-slate-700">These systems help ensure consistent message processing even during high traffic periods.</p>

        <h2 className="text-xl font-semibold text-slate-900">Third-Party Infrastructure</h2>
        <ul className="list-disc pl-5 space-y-1 text-slate-700">
          <li>Cloud hosting providers</li>
          <li>Telecom messaging networks</li>
          <li>Analytics and monitoring services</li>
        </ul>
        <p className="text-slate-700">All providers are evaluated for security and reliability standards. For more details, see our Subprocessor List.</p>

        <h2 className="text-xl font-semibold text-slate-900">Responsible Disclosure</h2>
        <p className="text-slate-700">If you discover a security issue, please report it to: security@textzy.in</p>

        <h2 className="text-xl font-semibold text-slate-900">Compliance Documents</h2>
        <ul className="list-disc pl-5 space-y-1 text-slate-700">
          <li>Privacy Policy</li>
          <li>Terms of Service</li>
          <li>Cookie Policy</li>
          <li>Messaging Compliance Policy</li>
          <li>Acceptable Use Policy</li>
          <li>Data Processing Agreement</li>
          <li>Security Policy</li>
          <li>Subprocessor List</li>
        </ul>

        <h2 className="text-xl font-semibold text-slate-900">Contact</h2>
        <p className="text-slate-700">Moneyart Private Limited</p>
        <p className="text-slate-700">Email: legal@textzy.in</p>
        <p className="text-slate-700">Website: https://textzy.in</p>
      </div>
    </StaticShell>
  );
}
