import { useState } from "react";
import { Link } from "react-router-dom";
import { ArrowLeft, Mail, MapPin, Phone, Send } from "lucide-react";
import StaticShell from "@/components/marketing/StaticShell";
import { useBranding } from "@/hooks/useBranding";

export default function ContactPage() {
  const { brand } = useBranding();
  const [form, setForm] = useState({ name: "", email: "", phone: "", message: "" });
  const [status, setStatus] = useState("idle");

  const submit = async (e) => {
    e.preventDefault();
    setStatus("sending");
    try {
      const apiBase = (typeof window !== "undefined" ? window._APP_CONFIG_?.API_BASE : "") || "/api";
      const base = apiBase.replace(/\/+$/, "");
      const res = await fetch(`${base}/public/contact`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ ...form, channel: "contact-page", brand: brand.name }),
      });
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      setStatus("sent");
      setForm({ name: "", email: "", phone: "", message: "" });
    } catch {
      // Fallback: open mailto so the user can still reach support
      const subject = encodeURIComponent(`[Contact] ${form.name || ""}`.trim() || "Contact request");
      const body = encodeURIComponent(
        `Name: ${form.name}\nEmail: ${form.email}\nPhone/WhatsApp: ${form.phone}\nMessage:\n${form.message}`,
      );
      if (brand.email) {
        window.location.href = `mailto:${brand.email}?subject=${subject}&body=${body}`;
      }
      setStatus("error");
    }
  };

  return (
    <StaticShell>
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-12 space-y-8">
        <div className="flex items-center gap-3 text-slate-500 text-sm">
          <Link to="/" className="flex items-center gap-2 text-orange-500 hover:text-orange-600">
            <ArrowLeft className="w-4 h-4" /> Back to home
          </Link>
          <span>-</span>
          <span>Contact {brand.name}</span>
        </div>
        <h1 className="text-4xl font-bold text-slate-900">Talk to our support desk</h1>
        <div className="grid lg:grid-cols-2 gap-8 items-start">
          <form onSubmit={submit} className="space-y-3">
            <div className="grid md:grid-cols-2 gap-3">
              <input required value={form.name} onChange={(e)=>setForm(p=>({...p,name:e.target.value}))} placeholder="Full name" className="w-full rounded-lg border border-slate-200 px-3 py-3 text-sm focus:border-orange-500 focus:outline-none" />
              <input required type="email" value={form.email} onChange={(e)=>setForm(p=>({...p,email:e.target.value}))} placeholder="Work email" className="w-full rounded-lg border border-slate-200 px-3 py-3 text-sm focus:border-orange-500 focus:outline-none" />
            </div>
            <input value={form.phone} onChange={(e)=>setForm(p=>({...p,phone:e.target.value}))} placeholder="Phone / WhatsApp (optional)" className="w-full rounded-lg border border-slate-200 px-3 py-3 text-sm focus:border-orange-500 focus:outline-none" />
            <textarea required value={form.message} onChange={(e)=>setForm(p=>({...p,message:e.target.value}))} rows={4} placeholder="What do you need help with?" className="w-full rounded-lg border border-slate-200 px-3 py-3 text-sm focus:border-orange-500 focus:outline-none" />
            <button type="submit" className="bg-orange-500 hover:bg-orange-600 text-white font-semibold px-6 py-3 rounded-lg">
              {status==="sending"?"Sending...":"Submit Ticket"}
            </button>
            {status==="sent" && <p className="text-green-600 text-sm">Ticket submitted. We'll follow up on email/WhatsApp.</p>}
            {status==="error" && <p className="text-red-600 text-sm">Could not submit. Please email {brand.email}.</p>}
          </form>
          <div className="bg-white rounded-2xl shadow-lg border border-slate-200 p-6 space-y-3">
            <h3 className="font-semibold text-slate-900">WhatsApp Support</h3>
            <p className="text-slate-600 text-sm">Scan to chat with the {brand.name} support desk.</p>
            <div className="flex items-center gap-6">
              <img src={brand.whatsappQr} alt="WhatsApp support QR" className="w-36 h-36 rounded-xl border border-slate-200 bg-white" />
              <div className="space-y-2 text-slate-700 text-sm">
                <div className="flex items-center gap-2">
                  <Send className="w-4 h-4 text-orange-500" />
                  <a href={`https://wa.me/${brand.whatsapp.replace(/\\D/g,"")}`} target="_blank" rel="noreferrer" className="hover:text-orange-600">
                    WhatsApp {brand.whatsapp}
                  </a>
                </div>
                <div className="flex items-center gap-2">
                  <Phone className="w-4 h-4 text-orange-500" />
                  <a href={`tel:${brand.phone?.replace(/\\s+/g,"")}`} className="hover:text-orange-600">
                    {brand.phone}
                  </a>
                </div>
                <div className="flex items-center gap-2">
                  <Mail className="w-4 h-4 text-orange-500" />
                  <a href={`mailto:${brand.email}`} className="hover:text-orange-600">
                    {brand.email}
                  </a>
                </div>
                <div className="flex items-start gap-2">
                  <MapPin className="w-4 h-4 text-orange-500 mt-0.5" /> <span>{brand.address}</span>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </StaticShell>
  );
}
