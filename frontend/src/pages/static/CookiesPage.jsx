import { Link } from "react-router-dom";
import { ArrowLeft } from "lucide-react";
import StaticShell from "@/components/marketing/StaticShell";

export default function CookiesPage() {
  return (
    <StaticShell>
      <div className="max-w-4xl mx-auto px-6 py-10 space-y-6">
        <div className="flex items-center gap-3 text-slate-500 text-sm">
          <Link to="/" className="flex items-center gap-2 text-orange-500 hover:text-orange-600">
            <ArrowLeft className="w-4 h-4" /> Back to home
          </Link>
          <span>·</span>
          <span>Cookies Policy</span>
        </div>
        <h1 className="text-3xl font-bold text-slate-900">Cookie Policy</h1>
        <p className="text-sm text-slate-500">Last Updated: March 10, 2026</p>
        <p className="text-slate-700">
          This Cookie Policy explains how Textzy uses cookies and similar technologies.
        </p>

        <div className="space-y-4">
          <h2 className="text-xl font-semibold text-slate-900">What Are Cookies</h2>
          <p className="text-slate-700">Cookies are small text files stored on your device when you visit a website. They help improve website performance and user experience.</p>
        </div>

        <div className="space-y-4">
          <h2 className="text-xl font-semibold text-slate-900">Types of Cookies We Use</h2>
          <p className="text-slate-700 font-medium">Essential Cookies</p>
          <ul className="list-disc pl-5 space-y-1 text-slate-700">
            <li>Login sessions</li>
            <li>Authentication</li>
            <li>Security protection</li>
          </ul>
          <p className="text-slate-700 font-medium">Performance Cookies</p>
          <ul className="list-disc pl-5 space-y-1 text-slate-700">
            <li>Analyze website traffic</li>
            <li>Improve performance (analytics tools that track page usage)</li>
          </ul>
          <p className="text-slate-700 font-medium">Preference Cookies</p>
          <ul className="list-disc pl-5 space-y-1 text-slate-700">
            <li>Language</li>
            <li>Login state</li>
            <li>UI preferences</li>
          </ul>
          <p className="text-slate-700 font-medium">Security Cookies</p>
          <ul className="list-disc pl-5 space-y-1 text-slate-700">
            <li>Protect the platform from malicious activity and unauthorized access</li>
          </ul>
        </div>

        <div className="space-y-4">
          <h2 className="text-xl font-semibold text-slate-900">Managing Cookies</h2>
          <p className="text-slate-700">
            You can control cookies through your browser settings. Disabling cookies may affect certain website features.
          </p>
        </div>
      </div>
    </StaticShell>
  );
}
