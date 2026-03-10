import { Link } from "react-router-dom";
import { ArrowLeft } from "lucide-react";
import StaticShell from "@/components/marketing/StaticShell";

export default function MessagingCompliancePage() {
  return (
    <StaticShell>
      <div className="max-w-4xl mx-auto px-6 py-10 space-y-6">
        <div className="flex items-center gap-3 text-slate-500 text-sm">
          <Link to="/" className="flex items-center gap-2 text-orange-500 hover:text-orange-600">
            <ArrowLeft className="w-4 h-4" /> Back to home
          </Link>
          <span>·</span>
          <span>Messaging Compliance</span>
        </div>
        <h1 className="text-3xl font-bold text-slate-900">Messaging Compliance Policy</h1>
        <p className="text-sm text-slate-500">Last Updated: March 10, 2026</p>
        <p className="text-slate-700">
          This Messaging Compliance Policy outlines the rules and requirements for sending messages using Textzy, a messaging platform operated by
          Moneyart Private Limited. All customers using Textzy services must comply with this policy, as well as applicable regulations and messaging
          platform rules.
        </p>

        <h2 className="text-xl font-semibold text-slate-900">1. Purpose</h2>
        <ul className="list-disc pl-5 space-y-1 text-slate-700">
          <li>Complies with applicable telecom regulations</li>
          <li>Protects end users from spam and abuse</li>
          <li>Maintains messaging platform integrity</li>
          <li>Meets WhatsApp and SMS provider requirements</li>
        </ul>
        <p className="text-slate-700">
          Failure to comply with this policy may result in message blocking, account suspension, or termination.
        </p>

        <h2 className="text-xl font-semibold text-slate-900">2. Customer Consent (Opt-In Requirement)</h2>
        <p className="text-slate-700">Before sending messages using Textzy, customers must obtain clear and verifiable consent from recipients.</p>
        <ul className="list-disc pl-5 space-y-1 text-slate-700">
          <li>Website sign-up forms</li>
          <li>Mobile app registration</li>
          <li>SMS opt-in confirmation</li>
          <li>Customer service interactions</li>
          <li>Explicit WhatsApp opt-in consent</li>
        </ul>
        <p className="text-slate-700">Users must clearly understand what type of messages they will receive, which organization is sending them, and how frequently messages may be sent. Sending messages without consent is strictly prohibited.</p>

        <h2 className="text-xl font-semibold text-slate-900">3. Opt-Out Mechanisms</h2>
        <p className="text-slate-700">Recipients must have the ability to stop receiving messages.</p>
        <ul className="list-disc pl-5 space-y-1 text-slate-700">
          <li>Reply STOP for SMS messages</li>
          <li>Unsubscribe links in promotional messages</li>
          <li>WhatsApp opt-out instructions when applicable</li>
        </ul>
        <p className="text-slate-700">When a user opts out, messaging must stop immediately unless legally required communications apply.</p>

        <h2 className="text-xl font-semibold text-slate-900">4. Content Restrictions</h2>
        <p className="text-slate-700">The following content is strictly prohibited:</p>
        <ul className="list-disc pl-5 space-y-1 text-slate-700">
          <li>Illegal content</li>
          <li>Fraudulent or deceptive content</li>
          <li>Harmful or abusive content</li>
          <li>Restricted industries</li>
          <li>Malware or malicious links</li>
        </ul>
        <p className="text-slate-700">Textzy reserves the right to block messages that violate these restrictions.</p>

        <h2 className="text-xl font-semibold text-slate-900">5. WhatsApp Messaging Compliance</h2>
        <ul className="list-disc pl-5 space-y-1 text-slate-700">
          <li>Using approved message templates</li>
          <li>Sending messages only after user opt-in</li>
          <li>Respecting WhatsApp messaging categories</li>
          <li>Avoiding excessive messaging frequency</li>
          <li>Respecting user opt-out requests</li>
        </ul>
        <p className="text-slate-700">WhatsApp may limit or suspend accounts that violate these policies. Textzy is not responsible for restrictions imposed by WhatsApp.</p>

        <h2 className="text-xl font-semibold text-slate-900">6. SMS Messaging Compliance (TRAI / DLT)</h2>
        <ul className="list-disc pl-5 space-y-1 text-slate-700">
          <li>Registering sender IDs</li>
          <li>Registering message templates</li>
          <li>Maintaining customer consent records</li>
          <li>Sending messages only to approved recipients</li>
        </ul>
        <p className="text-slate-700">Messages that do not comply with DLT regulations may be rejected by telecom operators.</p>

        <h2 className="text-xl font-semibold text-slate-900">7. Message Frequency and Spam Prevention</h2>
        <p className="text-slate-700">Customers must avoid excessive or spam-like messaging.</p>
        <ul className="list-disc pl-5 space-y-1 text-slate-700">
          <li>Sending large volumes of unsolicited messages</li>
          <li>Repeatedly messaging users without engagement</li>
          <li>Sending duplicate promotional messages</li>
        </ul>
        <p className="text-slate-700">Textzy may impose rate limits or throttling controls to prevent abuse.</p>

        <h2 className="text-xl font-semibold text-slate-900">8. Monitoring and Enforcement</h2>
        <p className="text-slate-700">Textzy may monitor messaging activity to detect spam or abuse, ensure regulatory compliance, and protect platform integrity.</p>
        <ul className="list-disc pl-5 space-y-1 text-slate-700">
          <li>Message filtering or blocking</li>
          <li>Temporary suspension of messaging capabilities</li>
          <li>Account termination for repeated violations</li>
        </ul>

        <h2 className="text-xl font-semibold text-slate-900">9. Customer Responsibility</h2>
        <ul className="list-disc pl-5 space-y-1 text-slate-700">
          <li>Maintaining records of user consent</li>
          <li>Ensuring message content complies with regulations</li>
          <li>Managing opt-out requests promptly</li>
          <li>Complying with WhatsApp and telecom provider rules</li>
        </ul>

        <h2 className="text-xl font-semibold text-slate-900">10. Reporting Abuse</h2>
        <p className="text-slate-700">Email: legal@textzy.in</p>

        <h2 className="text-xl font-semibold text-slate-900">11. Policy Updates</h2>
        <p className="text-slate-700">Textzy may update this Messaging Compliance Policy periodically to reflect regulatory or platform changes.</p>
      </div>
    </StaticShell>
  );
}
