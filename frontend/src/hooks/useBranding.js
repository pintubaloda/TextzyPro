import { useEffect, useState, useMemo } from "react";

const defaults = {
  name: "Textzy",
  tagline: "Business inbox for WhatsApp & SMS.",
  companyLine: "Textzy is a brand of Moneyart Private Limited.",
  address: "No-203, Wing A-2, Omkar Nandan, near Hotel Deccan Pavilion, opposite to Navale Bridge, Narhe, Pune, Maharashtra 411041",
  phone: "+919226508304",
  email: "helpdesk@moneyart.in",
  whatsapp: "+917249630121",
  whatsappQr: "",
  logoUrl: "",
};

const appConfigBrand = (() => {
  if (typeof window === "undefined") return defaults;
  const c = window._APP_CONFIG_ || {};
  const apiBase = c.API_BASE || "/api";
  return {
    name: c.BRAND_NAME || defaults.name,
    tagline: c.BRAND_TAGLINE || defaults.tagline,
    companyLine: c.BRAND_COMPANY_LINE || defaults.companyLine,
    address: c.BRAND_ADDRESS || defaults.address,
    phone: c.BRAND_PHONE || defaults.phone,
    email: c.BRAND_EMAIL || defaults.email,
    whatsapp: c.BRAND_WHATSAPP || defaults.whatsapp,
    whatsappQr: c.BRAND_WHATSAPP_QR || defaults.whatsappQr,
    logoUrl: c.BRAND_LOGO_URL || "",
    apiBase,
  };
})();

export function useBranding() {
  const [brand, setBrand] = useState(appConfigBrand);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    let active = true;
    const load = async () => {
      setLoading(true);
      try {
        const base = (typeof window !== "undefined" ? window._APP_CONFIG_?.API_BASE : "") || "/api";
        const res = await fetch(`${base.replace(/\/+$/, "")}/public/platform-branding`, { credentials: "include" });
        if (res.ok) {
          const data = await res.json().catch(() => ({}));
          if (!active) return;
          setBrand((prev) => {
            const phone = data.supportPhone || data.billingPhone || prev.phone;
            const whatsapp = data.supportWhatsappNo || data.whatsapp || phone || prev.whatsapp;
            return {
              ...prev,
              name: data.platformName || data.name || prev.name,
              tagline: data.tagline || data.description || prev.tagline,
              companyLine: data.companyLine || data.legalName || prev.companyLine,
              address: data.billingAddress || data.address || data.contactAddress || prev.address,
              phone,
              email: data.supportEmail || data.billingEmail || prev.email,
              whatsapp,
              whatsappQr: data.whatsappQr || prev.whatsappQr,
              logoUrl: data.logoUrl || prev.logoUrl,
            };
          });
        }
      } catch {
        /* ignore */
      } finally {
        if (active) setLoading(false);
      }
    };
    load();
    return () => {
      active = false;
    };
  }, []);

  const withQr = useMemo(() => {
    const num = (brand.whatsapp || "").replace(/\D/g, "");
    const qr = brand.whatsappQr || `https://api.qrserver.com/v1/create-qr-code/?size=240x240&data=${encodeURIComponent(`https://wa.me/${num}`)}`;
    return qr;
  }, [brand.whatsappQr, brand.whatsapp]);

  return { brand: { ...brand, whatsappQr: withQr }, loading };
}
