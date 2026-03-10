import { Link } from "react-router-dom";
import { ArrowLeft } from "lucide-react";
import StaticShell from "@/components/marketing/StaticShell";

export default function SecurityPage() {
  return (
    <StaticShell>
      <div className="max-w-4xl mx-auto px-6 py-10 space-y-6">
        <div className="flex items-center gap-3 text-slate-500 text-sm">
          <Link to="/" className="flex items-center gap-2 text-orange-500 hover:text-orange-600">
            <ArrowLeft className="w-4 h-4" /> Back to home
          </Link>
          <span>·</span>
          <span>Security</span>
        </div>
        <h1 className="text-3xl font-bold text-slate-900">Security Policy</h1>
        <p className="text-sm text-slate-500">Last Updated: March 10, 2026</p>
        <p className="text-slate-700">
          Textzy, operated by Moneyart Private Limited, is committed to maintaining strong security practices to protect customer data, messaging
          infrastructure, and platform integrity.
        </p>

        <h2 className="text-xl font-semibold text-slate-900">1. Security Principles</h2>
        <ul className="list-disc pl-5 space-y-1 text-slate-700">
          <li>Data confidentiality</li>
          <li>System integrity</li>
          <li>Service availability</li>
          <li>Access control and accountability</li>
          <li>Continuous monitoring and improvement</li>
        </ul>

        <h2 className="text-xl font-semibold text-slate-900">2. Infrastructure Security</h2>
        <ul className="list-disc pl-5 space-y-1 text-slate-700">
          <li>Secure cloud hosting environments</li>
          <li>Network segmentation and firewall protection</li>
          <li>DDoS mitigation</li>
          <li>Infrastructure monitoring and alerting</li>
          <li>Regular system updates and patch management</li>
        </ul>

        <h2 className="text-xl font-semibold text-slate-900">3. Data Protection</h2>
        <ul className="list-disc pl-5 space-y-1 text-slate-700">
          <li>Encryption of data in transit using HTTPS/TLS</li>
          <li>Secure storage and access controls</li>
          <li>Restricted administrative access</li>
          <li>Audit logging for system actions</li>
          <li>Data minimization practices</li>
        </ul>

        <h2 className="text-xl font-semibold text-slate-900">4. Access Control</h2>
        <ul className="list-disc pl-5 space-y-1 text-slate-700">
          <li>Role-based access control (RBAC)</li>
          <li>Multi-factor authentication (2FA) for administrative access</li>
          <li>IP allow-listing for critical infrastructure</li>
          <li>Access monitoring and logging</li>
        </ul>

        <h2 className="text-xl font-semibold text-slate-900">5. Application Security</h2>
        <ul className="list-disc pl-5 space-y-1 text-slate-700">
          <li>Secure coding standards</li>
          <li>Vulnerability testing</li>
          <li>Input validation and request filtering</li>
          <li>CSRF protection mechanisms</li>
          <li>Authentication and session management safeguards</li>
        </ul>

        <h2 className="text-xl font-semibold text-slate-900">6. Monitoring and Incident Detection</h2>
        <ul className="list-disc pl-5 space-y-1 text-slate-700">
          <li>System health monitoring</li>
          <li>Suspicious login detection</li>
          <li>Abnormal messaging patterns</li>
          <li>Infrastructure alerts</li>
        </ul>

        <h2 className="text-xl font-semibold text-slate-900">7. Incident Response</h2>
        <p className="text-slate-700">
          We follow a structured response process that includes detection, containment, notification where required, remediation, and post-incident review.
        </p>

        <h2 className="text-xl font-semibold text-slate-900">8. Customer Security Responsibilities</h2>
        <ul className="list-disc pl-5 space-y-1 text-slate-700">
          <li>Securing account credentials</li>
          <li>Implementing strong passwords and access controls</li>
          <li>Protecting API keys and integration tokens</li>
          <li>Ensuring proper consent before sending messages</li>
        </ul>

        <h2 className="text-xl font-semibold text-slate-900">9. Responsible Disclosure</h2>
        <p className="text-slate-700">Email: security@textzy.in</p>

        <h2 className="text-xl font-semibold text-slate-900">10. Policy Updates</h2>
        <p className="text-slate-700">
          This Security Policy may be updated periodically to reflect improvements to security practices or regulatory requirements.
        </p>
      </div>
    </StaticShell>
  );
}
