import { Link } from "react-router-dom";
import { ArrowLeft } from "lucide-react";

export default function CookiesPage() {
  return (
    <div className="min-h-screen bg-white">
      <div className="max-w-4xl mx-auto px-6 py-10 space-y-6">
        <div className="flex items-center gap-3 text-slate-500 text-sm">
          <Link to="/" className="flex items-center gap-2 text-orange-500 hover:text-orange-600">
            <ArrowLeft className="w-4 h-4" /> Back to home
          </Link>
          <span>·</span>
          <span>Cookies Policy</span>
        </div>
        <h1 className="text-3xl font-bold text-slate-900">Cookies Policy</h1>
        <ul className="list-disc pl-5 space-y-2 text-slate-700 leading-relaxed">
          <li>We use essential cookies for authentication, CSRF protection, and session continuity.</li>
          <li>Analytics cookies (if enabled) are used to improve reliability; you can disable them in your browser.</li>
          <li>No marketing/ad tracking pixels are loaded from the landing page.</li>
        </ul>
      </div>
    </div>
  );
}
