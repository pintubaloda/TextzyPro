import { Link } from "react-router-dom";
import { MessageSquare, Phone, Mail, MapPin, Send } from "lucide-react";
import { useBranding } from "@/hooks/useBranding";

export default function StaticShell({ children }) {
  const { brand } = useBranding();
  return (
    <div className="min-h-screen bg-white flex flex-col">
      <header className="border-b border-slate-200 bg-white">
        <div className="max-w-6xl mx-auto px-6 h-16 flex items-center justify-between">
          <Link to="/" className="flex items-center gap-2">
            {brand.logoUrl ? (
              <img src={brand.logoUrl} alt={brand.name} className="h-9 w-9 rounded-lg object-cover" />
            ) : (
              <div className="h-9 w-9 rounded-lg bg-orange-500 flex items-center justify-center">
                <MessageSquare className="h-5 w-5 text-white" />
              </div>
            )}
            <span className="font-heading font-bold text-lg text-slate-900">{brand.name}</span>
          </Link>
          <nav className="flex items-center gap-6 text-sm text-slate-600">
            <Link to="/">Home</Link>
            <Link to="/about">About</Link>
            <Link to="/contact">Contact</Link>
            <Link to="/privacy">Privacy</Link>
            <Link to="/refund">Refund</Link>
            <Link to="/cookies">Cookies</Link>
          </nav>
        </div>
      </header>

      <main className="flex-1 w-full">{children}</main>

      <footer className="bg-slate-900 text-slate-300">
        <div className="max-w-6xl mx-auto px-6 py-10 grid md:grid-cols-4 gap-8">
          <div className="space-y-3">
            <div className="flex items-center gap-2">
              <div className="w-8 h-8 bg-orange-500 rounded-lg flex items-center justify-center">
                <MessageSquare className="w-4 h-4 text-white" />
              </div>
              <span className="font-heading font-bold text-lg text-white">{brand.name}</span>
            </div>
            <p className="text-sm text-slate-400">{brand.tagline}</p>
          </div>
          <div>
            <h4 className="font-semibold text-white mb-3">Product</h4>
            <ul className="space-y-2 text-sm">
              <li><a href="#features" className="hover:text-orange-400">WhatsApp API</a></li>
              <li><a href="#features" className="hover:text-orange-400">SMS Gateway</a></li>
              <li><a href="#features" className="hover:text-orange-400">Automation</a></li>
              <li><a href="#features" className="hover:text-orange-400">Analytics</a></li>
            </ul>
          </div>
          <div>
            <h4 className="font-semibold text-white mb-3">Company</h4>
            <ul className="space-y-2 text-sm">
              <li><Link to="/about" className="hover:text-orange-400">About Us</Link></li>
              <li><Link to="/contact" className="hover:text-orange-400">Contact</Link></li>
              <li><Link to="/privacy" className="hover:text-orange-400">Privacy</Link></li>
              <li><Link to="/refund" className="hover:text-orange-400">Refund</Link></li>
              <li><Link to="/cookies" className="hover:text-orange-400">Cookies</Link></li>
            </ul>
          </div>
          <div>
            <h4 className="font-semibold text-white mb-3">Contact</h4>
            <ul className="space-y-2 text-sm text-slate-300">
              <li className="flex items-start gap-2">
                <MapPin className="w-4 h-4 text-orange-400 mt-0.5" />
                <span>{brand.address}</span>
              </li>
              <li className="flex items-center gap-2">
                <Phone className="w-4 h-4 text-orange-400" />
                <a className="hover:text-orange-300" href={`tel:${brand.phone?.replace(/\\s+/g, "")}`}>{brand.phone}</a>
              </li>
              <li className="flex items-center gap-2">
                <Mail className="w-4 h-4 text-orange-400" />
                <a className="hover:text-orange-300" href={`mailto:${brand.email}`}>{brand.email}</a>
              </li>
              {brand.whatsapp ? (
                <li className="flex items-center gap-2">
                  <Send className="w-4 h-4 text-orange-400" />
                  <a className="hover:text-orange-300" href={`https://wa.me/${brand.whatsapp.replace(/\\D/g,"")}`} target="_blank" rel="noreferrer">
                    WhatsApp {brand.whatsapp}
                  </a>
                </li>
              ) : null}
            </ul>
          </div>
        </div>
        <div className="border-t border-slate-800 py-4">
          <div className="max-w-6xl mx-auto px-6 flex flex-col md:flex-row justify-between items-center gap-4 text-sm text-slate-500">
            <span>© 2024 {brand.name}. All rights reserved.</span>
            <div className="flex gap-4">
              <Link to="/privacy" className="hover:text-orange-400">Privacy</Link>
              <Link to="/refund" className="hover:text-orange-400">Refund</Link>
              <Link to="/cookies" className="hover:text-orange-400">Cookies</Link>
            </div>
          </div>
        </div>
      </footer>
    </div>
  );
}
