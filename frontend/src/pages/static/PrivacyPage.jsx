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
        <p className="text-sm text-slate-500">Last Updated: March 10, 2026</p>
        <p className="text-slate-600 leading-relaxed">
          Textzy (“Textzy”, “we”, “our”, or “us”) is a communication platform operated by Moneyart Private Limited.
          This Privacy Policy explains how we collect, use, disclose, and protect information when you visit our website
          https://textzy.in, use our services, or interact with our platform.
        </p>
        <p className="text-slate-600 leading-relaxed">
          By accessing or using Textzy services, you agree to the practices described in this Privacy Policy.
        </p>

        <div className="space-y-4">
          <h2 className="text-xl font-semibold text-slate-900">1. Information We Collect</h2>
          <p className="text-slate-700 font-medium">Personal Information</p>
          <ul className="list-disc pl-5 space-y-1 text-slate-700">
            <li>Name</li>
            <li>Email address</li>
            <li>Phone number</li>
            <li>Company name</li>
            <li>Billing or account details</li>
            <li>Support communications</li>
          </ul>
          <p className="text-slate-700 font-medium">Technical and Usage Data</p>
          <ul className="list-disc pl-5 space-y-1 text-slate-700">
            <li>IP address</li>
            <li>Browser type and version</li>
            <li>Device information</li>
            <li>Pages visited and time spent</li>
            <li>API usage logs and messaging activity</li>
          </ul>
          <p className="text-slate-700 font-medium">Messaging Data</p>
          <ul className="list-disc pl-5 space-y-1 text-slate-700">
            <li>Sender and recipient numbers</li>
            <li>Message templates and metadata</li>
            <li>Delivery status and analytics</li>
          </ul>
          <p className="text-slate-600">Textzy processes such information only to provide and maintain our services.</p>
        </div>

        <div className="space-y-4">
          <h2 className="text-xl font-semibold text-slate-900">2. How We Use Your Information</h2>
          <ul className="list-disc pl-5 space-y-1 text-slate-700">
            <li>Provide and maintain Textzy services</li>
            <li>Enable WhatsApp onboarding and template management</li>
            <li>Deliver SMS messages through DLT-compliant infrastructure</li>
            <li>Monitor system performance and message delivery</li>
            <li>Improve our platform and user experience</li>
            <li>Provide customer support and respond to inquiries</li>
            <li>Send service updates and account notifications</li>
            <li>Prevent fraud, abuse, or unauthorized activity</li>
          </ul>
        </div>

        <div className="space-y-4">
          <h2 className="text-xl font-semibold text-slate-900">3. Cookies and Tracking Technologies</h2>
          <p className="text-slate-700">
            Textzy may use cookies and similar tracking technologies to improve website functionality, analyze usage, remember preferences,
            and enhance security. You can disable cookies through your browser settings, but some features may not function properly.
          </p>
        </div>

        <div className="space-y-4">
          <h2 className="text-xl font-semibold text-slate-900">4. Data Sharing and Disclosure</h2>
          <p className="text-slate-700">We do not sell personal data. We may share information only in the following situations:</p>
          <ul className="list-disc pl-5 space-y-1 text-slate-700">
            <li>Service providers (infrastructure, messaging partners, analytics)</li>
            <li>Legal requirements or regulatory obligations</li>
            <li>Business transfers (merger, acquisition, restructuring)</li>
          </ul>
        </div>

        <div className="space-y-4">
          <h2 className="text-xl font-semibold text-slate-900">5. Data Security</h2>
          <ul className="list-disc pl-5 space-y-1 text-slate-700">
            <li>Encrypted connections (HTTPS/TLS)</li>
            <li>Role-based access controls</li>
            <li>Two-factor authentication (2FA)</li>
            <li>IP allowlisting for sensitive systems</li>
            <li>Audit logging and monitoring</li>
            <li>Infrastructure-level protection</li>
          </ul>
          <p className="text-slate-700">Despite our efforts, no online platform can guarantee absolute security.</p>
        </div>

        <div className="space-y-4">
          <h2 className="text-xl font-semibold text-slate-900">6. Data Retention</h2>
          <p className="text-slate-700">
            We retain personal and operational data only as long as necessary to provide services, meet legal requirements, and maintain platform
            security. When data is no longer required, it is securely deleted or anonymized.
          </p>
        </div>

        <div className="space-y-4">
          <h2 className="text-xl font-semibold text-slate-900">7. Your Privacy Rights</h2>
          <ul className="list-disc pl-5 space-y-1 text-slate-700">
            <li>Access personal information we hold about you</li>
            <li>Request correction of inaccurate information</li>
            <li>Request deletion of your personal data</li>
            <li>Withdraw consent where applicable</li>
          </ul>
          <p className="text-slate-700">To exercise these rights, please contact us using the information below.</p>
        </div>

        <div className="space-y-4">
          <h2 className="text-xl font-semibold text-slate-900">8. Third-Party Services</h2>
          <p className="text-slate-700">
            Textzy services may integrate with third-party platforms including messaging providers, telecom networks, analytics tools,
            or cloud infrastructure. We are not responsible for the privacy practices of third-party services.
          </p>
        </div>

        <div className="space-y-4">
          <h2 className="text-xl font-semibold text-slate-900">9. Children's Privacy</h2>
          <p className="text-slate-700">
            Textzy services are intended for businesses and individuals over the age of 18. We do not knowingly collect personal information from children.
          </p>
        </div>

        <div className="space-y-4">
          <h2 className="text-xl font-semibold text-slate-900">10. Updates to This Policy</h2>
          <p className="text-slate-700">
            We may update this Privacy Policy periodically to reflect changes in our services, legal requirements, or security practices.
            When changes are made, the “Last Updated” date at the top of this page will be revised.
          </p>
        </div>

        <div className="space-y-4">
          <h2 className="text-xl font-semibold text-slate-900">GDPR + WhatsApp Compliance Clauses</h2>
          <p className="text-slate-700 font-medium">GDPR Compliance (For EU Users)</p>
          <p className="text-slate-700">
            If you are located in the European Economic Area (EEA), we process personal data in accordance with the General Data Protection Regulation (GDPR).
            Users have rights including access, rectification, erasure, restriction, and data portability. Where required, data processing is based on
            contractual necessity, legal obligations, or legitimate interests.
          </p>
          <p className="text-slate-700 font-medium">WhatsApp Business Messaging Compliance</p>
          <p className="text-slate-700">
            Textzy provides infrastructure to send messages through the WhatsApp Business Platform. Users must comply with WhatsApp messaging policies,
            including obtaining user opt-in, sending only approved templates, respecting opt-out requests, and avoiding spam or promotional abuse.
            WhatsApp may suspend or restrict messaging accounts that violate their policies. Textzy is not responsible for restrictions imposed by WhatsApp or Meta platforms.
          </p>
        </div>

        <div className="space-y-2">
          <h2 className="text-xl font-semibold text-slate-900">11. Contact Us</h2>
          <p className="text-slate-700">Textzy</p>
          <p className="text-slate-700">Moneyart Private Limited</p>
          <p className="text-slate-700">Email: legal@textzy.in</p>
          <p className="text-slate-700">Website: https://textzy.in</p>
        </div>
      </div>
    </StaticShell>
  );
}
