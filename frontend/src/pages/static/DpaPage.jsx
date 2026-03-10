import { Link } from "react-router-dom";
import { ArrowLeft } from "lucide-react";
import StaticShell from "@/components/marketing/StaticShell";

export default function DpaPage() {
  return (
    <StaticShell>
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-10 space-y-6">
        <div className="flex items-center gap-3 text-slate-500 text-sm">
          <Link to="/" className="flex items-center gap-2 text-orange-500 hover:text-orange-600">
            <ArrowLeft className="w-4 h-4" /> Back to home
          </Link>
          <span>·</span>
          <span>Data Processing Agreement</span>
        </div>
        <h1 className="text-3xl font-bold text-slate-900">Data Processing Agreement (DPA)</h1>
        <p className="text-sm text-slate-500">Last Updated: March 10, 2026</p>
        <p className="text-slate-700">
          This Data Processing Agreement (“DPA”) forms part of the Terms of Service between Textzy, operated by Moneyart Private Limited, and the customer
          using Textzy services.
        </p>

        <h2 className="text-xl font-semibold text-slate-900">1. Definitions</h2>
        <ul className="list-disc pl-5 space-y-1 text-slate-700">
          <li>Controller: entity that determines the purpose and means of processing personal data</li>
          <li>Processor: entity that processes personal data on behalf of the controller</li>
          <li>Customer acts as the Data Controller</li>
          <li>Textzy acts as the Data Processor</li>
        </ul>

        <h2 className="text-xl font-semibold text-slate-900">2. Scope of Processing</h2>
        <p className="text-slate-700">
          Textzy processes personal data solely for providing messaging services including SMS delivery, WhatsApp messaging, message routing, delivery status
          reporting, and platform analytics. Processing is performed only as instructed by the customer.
        </p>

        <h2 className="text-xl font-semibold text-slate-900">3. Categories of Data</h2>
        <ul className="list-disc pl-5 space-y-1 text-slate-700">
          <li>Phone numbers</li>
          <li>Messaging metadata</li>
          <li>Message templates</li>
          <li>Delivery reports</li>
          <li>User account details</li>
        </ul>
        <p className="text-slate-700">Textzy does not determine message recipients or message content.</p>

        <h2 className="text-xl font-semibold text-slate-900">4. Customer Responsibilities</h2>
        <ul className="list-disc pl-5 space-y-1 text-slate-700">
          <li>Obtaining lawful consent from message recipients</li>
          <li>Ensuring messages comply with applicable regulations</li>
          <li>Maintaining records of consent when required</li>
        </ul>

        <h2 className="text-xl font-semibold text-slate-900">5. Security Measures</h2>
        <ul className="list-disc pl-5 space-y-1 text-slate-700">
          <li>Encrypted communications (TLS)</li>
          <li>Secure authentication</li>
          <li>Role-based access control</li>
          <li>IP allowlisting</li>
          <li>Infrastructure monitoring</li>
          <li>Audit logging</li>
        </ul>

        <h2 className="text-xl font-semibold text-slate-900">6. Subprocessors</h2>
        <p className="text-slate-700">
          Textzy may engage trusted subprocessors including cloud infrastructure providers, messaging network providers, and analytics services. All
          subprocessors are required to maintain appropriate data protection safeguards.
        </p>

        <h2 className="text-xl font-semibold text-slate-900">7. International Data Transfers</h2>
        <p className="text-slate-700">
          Where personal data is transferred outside the user's jurisdiction, Textzy will implement appropriate safeguards consistent with applicable data
          protection laws.
        </p>

        <h2 className="text-xl font-semibold text-slate-900">8. Data Retention</h2>
        <p className="text-slate-700">
          Personal data is retained only as long as necessary to provide services, maintain system reliability, and comply with legal obligations.
        </p>

        <h2 className="text-xl font-semibold text-slate-900">9. Data Subject Rights</h2>
        <p className="text-slate-700">
          Where applicable, Textzy will assist customers in responding to requests from individuals exercising rights such as access, correction, deletion,
          or restriction of processing. Requests may be directed to privacy@textzy.in.
        </p>

        <h2 className="text-xl font-semibold text-slate-900">10. Data Breach Notification</h2>
        <p className="text-slate-700">
          If Textzy becomes aware of a data breach affecting customer data, we will notify the customer without undue delay and take reasonable steps to
          mitigate the impact.
        </p>

        <h2 className="text-xl font-semibold text-slate-900">11. Termination</h2>
        <p className="text-slate-700">
          Upon termination of services, personal data may be deleted or returned to the customer in accordance with applicable law and operational
          requirements.
        </p>

        <h2 className="text-xl font-semibold text-slate-900">12. Contact</h2>
        <p className="text-slate-700">Email: legal@textzy.in</p>
        <p className="text-slate-700">Company: Moneyart Private Limited</p>
      </div>
    </StaticShell>
  );
}
