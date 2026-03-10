import { Link } from "react-router-dom";
import { MessageSquare, Phone, Mail, MapPin, Send } from "lucide-react";
import { useBranding } from "@/hooks/useBranding";

export default function StaticShell({ children }) {
  const { brand } = useBranding();
  return (
    <div className="min-h-screen bg-white flex flex-col">
      <header className="border-b border-slate-200 bg-white sticky top-0 z-50">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 h-16 flex items-center justify-between">
          <Link to="/" className="flex items-center gap-2">
            {brand.logoUrl ? (
              <img src={brand.logoUrl} alt={brand.name} className="h-8 w-8 rounded-lg object-cover" />
            ) : (
              <div className="h-8 w-8 rounded-lg bg-orange-500 flex items-center justify-center">
                <MessageSquare className="h-5 w-5 text-white" />
              </div>
            )}
            <span className="font-heading font-bold text-lg text-slate-900">{brand.name}</span>
          </Link>
          <nav className="hidden md:flex items-center gap-6 text-sm font-medium text-slate-600">
            <Link to="/" className="hover:text-orange-500 transition-colors">Home</Link>
            <Link to="/about" className="hover:text-orange-500 transition-colors">About</Link>
            <Link to="/contact" className="hover:text-orange-500 transition-colors">Contact</Link>
            <Link to="/privacy" className="hover:text-orange-500 transition-colors">Privacy</Link>
            <Link to="/refund" className="hover:text-orange-500 transition-colors">Refund</Link>
            <Link to="/cookies" className="hover:text-orange-500 transition-colors">Cookies</Link>
          </nav>
        </div>
      </header>

      <main className="flex-1 w-full">{children}</main>

      <footer className="bg-slate-900 text-slate-300">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-12 grid md:grid-cols-2 lg:grid-cols-4 gap-8">
          <div className="space-y-3">
            <div className="flex items-center gap-2">
              <div className="w-8 h-8 bg-orange-500 rounded-lg flex items-center justify-center">
                <MessageSquare className="w-4 h-4 text-white" />
              </div>
              <span className="font-heading font-bold text-lg text-white">{brand.name}</span>
            </div>
            <p className="text-sm text-slate-400 leading-relaxed">{brand.tagline}</p>
          </div>
          <div>
            <h4 className="font-semibold text-white mb-3">Product</h4>
            <ul className="space-y-2 text-sm">
              <li><a href="/#features" className="hover:text-orange-400 transition-colors">WhatsApp API</a></li>
              <li><a href="/#features" className="hover:text-orange-400 transition-colors">SMS Gateway</a></li>
              <li><a href="/#features" className="hover:text-orange-400 transition-colors">Automation</a></li>
              <li><a href="/#features" className="hover:text-orange-400 transition-colors">Analytics</a></li>
            </ul>
          </div>
          <div>
            <h4 className="font-semibold text-white mb-3">Company</h4>
            <ul className="space-y-2 text-sm">
              <li><Link to="/about" className="hover:text-orange-400 transition-colors">About Us</Link></li>
              <li><Link to="/contact" className="hover:text-orange-400 transition-colors">Contact</Link></li>
              <li><Link to="/privacy" className="hover:text-orange-400 transition-colors">Privacy Policy</Link></li>
              <li><Link to="/terms" className="hover:text-orange-400 transition-colors">Terms of Service</Link></li>
              <li><Link to="/refund" className="hover:text-orange-400 transition-colors">Refund Policy</Link></li>
              <li><Link to="/security" className="hover:text-orange-400 transition-colors">Security</Link></li>
              <li><Link to="/trust-center" className="hover:text-orange-400 transition-colors">Trust Center</Link></li>
            </ul>
          </div>
          <div>
            <h4 className="font-semibold text-white mb-3">Legal &amp; Compliance</h4>
            <ul className="space-y-2 text-sm mb-5">
              <li><Link to="/cookies" className="hover:text-orange-400 transition-colors">Cookies Policy</Link></li>
              <li><Link to="/dpdp" className="hover:text-orange-400 transition-colors">DPDP Act</Link></li>
              <li><Link to="/messaging-compliance" className="hover:text-orange-400 transition-colors">Messaging Compliance</Link></li>
              <li><Link to="/acceptable-use" className="hover:text-orange-400 transition-colors">Acceptable Use</Link></li>
              <li><Link to="/subprocessors" className="hover:text-orange-400 transition-colors">Subprocessors</Link></li>
              <li><Link to="/dpa" className="hover:text-orange-400 transition-colors">DPA</Link></li>
            </ul>
            <h4 className="font-semibold text-white mb-3">Contact</h4>
            <ul className="space-y-2 text-sm text-slate-300">
              <li className="flex items-start gap-2">
                <MapPin className="w-4 h-4 text-orange-400 mt-0.5 flex-shrink-0" />
                <span>{brand.address}</span>
              </li>
              <li className="flex items-center gap-2">
                <Phone className="w-4 h-4 text-orange-400 flex-shrink-0" />
                <a className="hover:text-orange-300 transition-colors" href={`tel:${brand.phone?.replace(/\s+/g, "")}`}>{brand.phone}</a>
              </li>
              <li className="flex items-center gap-2">
                <Mail className="w-4 h-4 text-orange-400 flex-shrink-0" />
                <a className="hover:text-orange-300 transition-colors" href={`mailto:${brand.email}`}>{brand.email}</a>
              </li>
              {brand.whatsapp ? (
                <li className="flex items-center gap-2">
                  <Send className="w-4 h-4 text-orange-400 flex-shrink-0" />
                  <a className="hover:text-orange-300 transition-colors" href={`https://wa.me/${brand.whatsapp.replace(/\D/g,"")}`} target="_blank" rel="noreferrer">
                    WhatsApp {brand.whatsapp}
                  </a>
                </li>
              ) : null}
            </ul>
          </div>
        </div>
        <div className="border-t border-slate-800 py-5">
          <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 flex flex-col md:flex-row justify-between items-center gap-3 text-sm text-slate-500">
            <span>(c) 2026 {brand.name} — Designed &amp; Developed with Love by Moneyart Private Limited</span>
            <div className="flex flex-wrap gap-4">
              <Link to="/privacy" className="hover:text-orange-400 transition-colors">Privacy</Link>
              <Link to="/terms" className="hover:text-orange-400 transition-colors">Terms</Link>
              <Link to="/refund" className="hover:text-orange-400 transition-colors">Refund</Link>
              <Link to="/cookies" className="hover:text-orange-400 transition-colors">Cookies</Link>
              <Link to="/dpdp" className="hover:text-orange-400 transition-colors">DPDP Act</Link>
            </div>
          </div>
        </div>
      </footer>
    </div>
  );
}
