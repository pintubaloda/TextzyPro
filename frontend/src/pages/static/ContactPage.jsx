import { useState } from "react";
import { Link } from "react-router-dom";
import { ArrowLeft, Mail, MapPin, Phone } from "lucide-react";

const brandFromConfig = () => {
  const cfg = (typeof window !== "undefined" ? window._APP_CONFIG_ : {}) || {};
  return {
    name: cfg.BRAND_NAME || "Textzy",
    address: cfg.BRAND_ADDRESS || "Mumbai, India",
    phone: cfg.BRAND_PHONE || "+91 22 1234 5678",
    email: cfg.BRAND_EMAIL || "hello@textzy.in",
    whatsapp: cfg.BRAND_WHATSAPP || "+919867530000",
    whatsappQr: cfg.BRAND_WHATSAPP_QR || `https://api.qrserver.com/v1/create-qr-code/?size=240x240&data=${encodeURIComponent(`https://wa.me/${(cfg.BRAND_WHATSAPP || "+919867530000").replace(/\D/g, "")}`)}`,
    apiBase: cfg.API_BASE || "/api",
  };
};

export default function ContactPage() {
  const brand = brandFromConfig();
  const [form, setForm] = useState({ name: "", email: "", phone: "", message: "" });
  const [status, setStatus] = useState("idle");

  const submit = async (e) => {
    e.preventDefault();
    setStatus("sending");
    try {
      const res = await fetch(`${brand.apiBase.replace(/\/$/, "")}/api/public/contact`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ ...form, channel: "contact-page", brand: brand.name }),
      });
      if (!res.ok) throw new Error("fail");
      setStatus("sent");
      setForm({ name: "", email: "", phone: "", message: "" });
    } catch {
      setStatus("error");
    }
  };

  return (
    <div className="min-h-screen bg-slate-50">
      <div className="max-w-5xl mx-auto px-6 py-10 space-y-8">
        <div className="flex items-center gap-3 text-slate-500 text-sm">
          <Link to="/" className="flex items-center gap-2 text-orange-500 hover:text-orange-600">
            <ArrowLeft className="w-4 h-4" /> Back to home
          </Link>
          <span>·</span>
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
            {status==="sent" && <p className="text-green-600 text-sm">Ticket submitted. We’ll follow up on email/WhatsApp.</p>}
            {status==="error" && <p className="text-red-600 text-sm">Could not submit. Please email {brand.email}.</p>}
          </form>
          <div className="bg-white rounded-2xl shadow-lg border border-slate-200 p-6 space-y-3">
            <h3 className="font-semibold text-slate-900">WhatsApp Support</h3>
            <p className="text-slate-600 text-sm">Scan to chat with the {brand.name} support desk.</p>
            <div className="flex items-center gap-6">
              <img src={brand.whatsappQr} alt="WhatsApp support QR" className="w-36 h-36 rounded-xl border border-slate-200 bg-white" />
              <div className="space-y-2 text-slate-700 text-sm">
                <div className="flex items-center gap-2"><Phone className="w-4 h-4 text-orange-500" /> {brand.whatsapp}</div>
                <div className="flex items-center gap-2"><Mail className="w-4 h-4 text-orange-500" /> {brand.email}</div>
                <div className="flex items-center gap-2"><MapPin className="w-4 h-4 text-orange-500" /> {brand.address}</div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
