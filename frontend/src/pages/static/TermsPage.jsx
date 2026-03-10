import { Link } from "react-router-dom";
import { ArrowLeft } from "lucide-react";
import StaticShell from "@/components/marketing/StaticShell";

export default function TermsPage() {
  return (
    <StaticShell>
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-10 space-y-6">
        <div className="flex items-center gap-3 text-slate-500 text-sm">
          <Link to="/" className="flex items-center gap-2 text-orange-500 hover:text-orange-600">
            <ArrowLeft className="w-4 h-4" /> Back to home
          </Link>
          <span>·</span>
          <span>Terms of Service</span>
        </div>
        <h1 className="text-3xl font-bold text-slate-900">Terms of Service</h1>
        <p className="text-sm text-slate-500">Last Updated: March 10, 2026</p>
        <p className="text-slate-700">
          These Terms of Service (“Terms”) govern your access to and use of Textzy, a messaging platform operated by Moneyart Private Limited (“Textzy”,
          “we”, “our”, or “us”). By accessing or using our services, you agree to be bound by these Terms.
        </p>

        <div className="space-y-4">
          <h2 className="text-xl font-semibold text-slate-900">1. Services Provided</h2>
          <p className="text-slate-700">Textzy provides a communication platform that enables businesses to send and manage customer communications through:</p>
          <ul className="list-disc pl-5 space-y-1 text-slate-700">
            <li>WhatsApp Business messaging</li>
            <li>SMS messaging (DLT compliant infrastructure)</li>
            <li>Real-time messaging inbox</li>
            <li>Messaging APIs and integrations</li>
            <li>Analytics and delivery monitoring</li>
          </ul>
          <p className="text-slate-700">We may modify or update services at any time.</p>
        </div>

        <div className="space-y-4">
          <h2 className="text-xl font-semibold text-slate-900">2. Account Registration</h2>
          <ul className="list-disc pl-5 space-y-1 text-slate-700">
            <li>Provide accurate and complete information</li>
            <li>Maintain the security of your account credentials</li>
            <li>Notify us immediately of unauthorized access</li>
          </ul>
          <p className="text-slate-700">You are responsible for all activity that occurs under your account.</p>
        </div>

        <div className="space-y-4">
          <h2 className="text-xl font-semibold text-slate-900">3. Acceptable Use</h2>
          <ul className="list-disc pl-5 space-y-1 text-slate-700">
            <li>Sending spam or unsolicited messages</li>
            <li>Fraudulent or deceptive communications</li>
            <li>Illegal or harmful content</li>
            <li>Harassment or abuse</li>
            <li>Circumventing telecom or messaging regulations</li>
            <li>Sending content prohibited by WhatsApp or telecom authorities</li>
          </ul>
          <p className="text-slate-700">Textzy reserves the right to suspend or terminate accounts violating these rules.</p>
        </div>

        <div className="space-y-4">
          <h2 className="text-xl font-semibold text-slate-900">4. Messaging Compliance</h2>
          <ul className="list-disc pl-5 space-y-1 text-slate-700">
            <li>TRAI regulations</li>
            <li>DLT registration requirements</li>
            <li>WhatsApp Business messaging policies</li>
            <li>Data protection and privacy laws</li>
          </ul>
          <p className="text-slate-700">Users are responsible for obtaining required customer consent (opt-in) before sending messages.</p>
        </div>

        <div className="space-y-4">
          <h2 className="text-xl font-semibold text-slate-900">5. Payment and Billing</h2>
          <ul className="list-disc pl-5 space-y-1 text-slate-700">
            <li>Fees are billed according to the pricing plan selected</li>
            <li>Messaging charges depend on volume and provider rates</li>
            <li>Fees are non-refundable unless required by law</li>
          </ul>
          <p className="text-slate-700">We may update pricing with reasonable notice.</p>
        </div>

        <div className="space-y-4">
          <h2 className="text-xl font-semibold text-slate-900">6. Intellectual Property</h2>
          <p className="text-slate-700">
            All platform software, branding, and technology used in Textzy are owned by Moneyart Private Limited or its licensors. Users may not reverse
            engineer, copy platform functionality, or redistribute the service without authorization.
          </p>
        </div>

        <div className="space-y-4">
          <h2 className="text-xl font-semibold text-slate-900">7. Service Availability</h2>
          <p className="text-slate-700">
            We aim to maintain reliable services but cannot guarantee uninterrupted access. We are not liable for service interruptions caused by telecom
            providers, WhatsApp platform changes, infrastructure outages, or force majeure events.
          </p>
        </div>

        <div className="space-y-4">
          <h2 className="text-xl font-semibold text-slate-900">8. Limitation of Liability</h2>
          <p className="text-slate-700">
            To the maximum extent permitted by law, Moneyart Private Limited shall not be liable for indirect or consequential damages, lost revenue or
            lost data, or damages caused by third-party messaging networks.
          </p>
        </div>

        <div className="space-y-4">
          <h2 className="text-xl font-semibold text-slate-900">9. Termination</h2>
          <p className="text-slate-700">
            We may suspend or terminate accounts if Terms are violated, messaging regulations are breached, or the platform is used for illegal purposes.
            Users may terminate their account at any time.
          </p>
        </div>

        <div className="space-y-4">
          <h2 className="text-xl font-semibold text-slate-900">10. Governing Law</h2>
          <p className="text-slate-700">
            These Terms shall be governed by the laws of India. Any disputes shall be subject to the jurisdiction of courts located in India.
          </p>
        </div>

        <div className="space-y-2">
          <h2 className="text-xl font-semibold text-slate-900">11. Contact</h2>
          <p className="text-slate-700">Email: legal@textzy.in</p>
          <p className="text-slate-700">Company: Moneyart Private Limited</p>
        </div>
      </div>
    </StaticShell>
  );
}
