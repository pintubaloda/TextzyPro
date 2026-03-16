import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { BookOpenText, Copy, ExternalLink, FileCode2, Globe2, MessageSquareText } from "lucide-react";
import { toast } from "sonner";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";

const DOCS = {
  sms: {
    title: "SMS API Reference",
    description: "Tenant-scoped SMS API documentation with public send examples, authentication, DLT fields, status model, and implementation patterns.",
    html: "/docs/sms-api-reference.html",
    markdown: "/docs/SMS_API_REFERENCE.md",
    tone: "from-orange-50 via-white to-amber-50",
    chip: "SMS",
  },
  whatsapp: {
    title: "WhatsApp API Reference",
    description: "WhatsApp API documentation with public send examples, authenticated messaging flows, templates, interactive messages, media, and automation operations.",
    html: "/docs/whatsapp-api-reference.html",
    markdown: "/docs/WHATSAPP_API_REFERENCE.md",
    tone: "from-cyan-50 via-white to-emerald-50",
    chip: "WhatsApp",
  },
  kyc: {
    title: "KYC API Reference",
    description: "KYC session API documentation (DigiLocker plugin) including session creation, redirect flow, callback model, webhook payload, and credit-based billing behavior.",
    html: "/docs/kyc-api-reference.html",
    markdown: "/docs/KYC_API_REFERENCE.md",
    tone: "from-emerald-50 via-white to-lime-50",
    chip: "KYC",
  },
};

function getDoc(type) {
  return DOCS[type] || DOCS.sms;
}

async function copyText(text, label) {
  await navigator.clipboard.writeText(text);
  toast.success(`${label} copied`);
}

function enhanceDocument(doc) {
  const body = doc?.body;
  if (!body) return { sections: [], html: "" };

  Array.from(body.querySelectorAll("h2, h3")).forEach((heading, index) => {
    if (!heading.id) {
      const slug = String(heading.textContent || `section-${index}`)
        .toLowerCase()
        .trim()
        .replace(/[^a-z0-9]+/g, "-")
        .replace(/^-+|-+$/g, "");
      heading.id = slug || `section-${index}`;
    }
    heading.classList.add("textzy-anchor-target");
  });

  const sections = Array.from(body.querySelectorAll("h2, h3")).map((heading, index) => ({
    id: heading.id || `section-${index}`,
    label: (heading.textContent || `Section ${index + 1}`).trim(),
    level: heading.tagName.toLowerCase() === "h2" ? 2 : 3,
  }));

  return {
    sections,
    html: body.innerHTML,
  };
}

export default function ApiDocsViewer({ open, onOpenChange, type, onTypeChange }) {
  const docMeta = useMemo(() => getDoc(type), [type]);
  const [sections, setSections] = useState([]);
  const [activeSection, setActiveSection] = useState("");
  const [htmlContent, setHtmlContent] = useState("");
  const [loading, setLoading] = useState(false);
  const topLevelSections = useMemo(() => sections.filter((section) => section.level === 2), [sections]);
  const contentRef = useRef(null);

  useEffect(() => {
    if (!open) return;
    let cancelled = false;

    const load = async () => {
      try {
        setLoading(true);
        setSections([]);
        setActiveSection("");
        const response = await fetch(docMeta.html, { credentials: "same-origin" });
        if (!response.ok) throw new Error(`Failed to load documentation (${response.status})`);
        const html = await response.text();
        const parser = new DOMParser();
        const parsed = parser.parseFromString(html, "text/html");
        const enhanced = enhanceDocument(parsed);
        if (cancelled) return;
        setHtmlContent(enhanced.html);
        setSections(enhanced.sections);
        setActiveSection(enhanced.sections[0]?.id || "");
      } catch (error) {
        if (!cancelled) {
          setHtmlContent(`
            <div class="textzy-doc-error">
              <h2>Documentation unavailable</h2>
              <p>${error?.message || "Failed to load documentation."}</p>
            </div>
          `);
          setSections([]);
          setActiveSection("");
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    };

    load();
    return () => {
      cancelled = true;
    };
  }, [docMeta.html, open]);

  useEffect(() => {
    const container = contentRef.current;
    if (!container) return;

    const styleId = "textzy-doc-inline-style";
    if (!container.querySelector(`#${styleId}`)) {
      const style = document.createElement("style");
      style.id = styleId;
      style.textContent = `
        .textzy-doc-surface{padding:28px;min-height:100%;background:#fff;color:#0f172a}
        .textzy-doc-surface .wrap{max-width:1200px;margin:0 auto;padding:0}
        .textzy-doc-surface .hero{margin-bottom:20px}
        .textzy-doc-surface .textzy-anchor-target{scroll-margin-top:24px}
        .textzy-doc-surface .textzy-doc-error{display:flex;min-height:320px;flex-direction:column;align-items:center;justify-content:center;text-align:center;color:#475569}
        .textzy-pre-wrap{position:relative;margin-top:12px}
        .textzy-copy-btn{position:absolute;top:10px;right:10px;z-index:4;border:1px solid rgba(226,232,240,.9);background:rgba(255,255,255,.96);color:#0f172a;border-radius:10px;font-size:12px;font-weight:700;padding:6px 10px;cursor:pointer;box-shadow:0 8px 18px rgba(15,23,42,.08)}
        .textzy-copy-btn:hover{background:#fff7ed;color:#c2410c}
      `;
      container.prepend(style);
    }

    Array.from(container.querySelectorAll("pre")).forEach((pre, index) => {
      if (pre.parentElement?.classList.contains("textzy-pre-wrap")) return;
      const wrapper = document.createElement("div");
      wrapper.className = "textzy-pre-wrap";
      pre.parentNode.insertBefore(wrapper, pre);
      wrapper.appendChild(pre);

      const button = document.createElement("button");
      button.type = "button";
      button.className = "textzy-copy-btn";
      button.textContent = "Copy";
      button.setAttribute("aria-label", `Copy code block ${index + 1}`);
      button.onclick = async () => {
        try {
          await navigator.clipboard.writeText(pre.innerText || pre.textContent || "");
          button.textContent = "Copied";
          setTimeout(() => {
            button.textContent = "Copy";
          }, 1200);
        } catch {
          button.textContent = "Failed";
          setTimeout(() => {
            button.textContent = "Copy";
          }, 1200);
        }
      };
      wrapper.appendChild(button);
    });

    const headings = Array.from(container.querySelectorAll("h2, h3"));
    if (!headings.length) return;

    const observer = new IntersectionObserver(
      (entries) => {
        const visible = entries
          .filter((entry) => entry.isIntersecting)
          .sort((a, b) => a.boundingClientRect.top - b.boundingClientRect.top)[0];
        if (visible?.target?.id) setActiveSection(visible.target.id);
      },
      { root: container, rootMargin: "-10% 0px -70% 0px", threshold: [0, 1] }
    );

    headings.forEach((heading) => observer.observe(heading));
    return () => observer.disconnect();
  }, [htmlContent]);

  const jumpToSection = useCallback((id) => {
    const container = contentRef.current;
    const target = container?.querySelector(`#${CSS.escape(id)}`);
    if (target) {
      target.scrollIntoView({ behavior: "smooth", block: "start" });
      setActiveSection(id);
    }
  }, []);

  const handleTypeChange = (nextType) => {
    setHtmlContent("");
    setSections([]);
    setActiveSection("");
    onTypeChange(nextType);
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-[96vw] overflow-hidden border-slate-200 p-0 sm:max-w-[96vw]">
        <DialogHeader className={`border-b border-slate-200 bg-gradient-to-r ${docMeta.tone} px-6 py-5`}>
          <div className="flex flex-col gap-4 xl:flex-row xl:items-start xl:justify-between">
            <div className="space-y-2">
              <div className="flex items-center gap-2">
                <Badge className="rounded-full bg-slate-900 px-3 py-1 text-white hover:bg-slate-900">
                  <BookOpenText className="mr-1.5 h-3.5 w-3.5" />
                  {docMeta.chip}
                </Badge>
                <Badge variant="outline" className="rounded-full border-slate-300 bg-white/80 text-slate-700">
                  Postman-style documentation
                </Badge>
              </div>
              <DialogTitle className="text-2xl font-bold text-slate-950">{docMeta.title}</DialogTitle>
              <DialogDescription className="max-w-3xl text-sm text-slate-600">
                {docMeta.description}
              </DialogDescription>
              <div className="grid gap-3 pt-2 sm:grid-cols-3">
                <div className="rounded-2xl border border-white/70 bg-white/75 px-4 py-3">
                  <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-slate-500">Primary Sections</p>
                  <p className="mt-1 text-lg font-bold text-slate-950">{topLevelSections.length || "-"}</p>
                </div>
                <div className="rounded-2xl border border-white/70 bg-white/75 px-4 py-3">
                  <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-slate-500">Formats</p>
                  <p className="mt-1 text-lg font-bold text-slate-950">HTML + Markdown</p>
                </div>
                <div className="rounded-2xl border border-white/70 bg-white/75 px-4 py-3">
                  <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-slate-500">Best Use</p>
                  <p className="mt-1 text-lg font-bold text-slate-950">Integrate Faster</p>
                </div>
              </div>
            </div>
            <div className="flex flex-wrap gap-2">
              <Button variant={type === "sms" ? "default" : "outline"} className={type === "sms" ? "bg-orange-500 hover:bg-orange-600" : ""} onClick={() => handleTypeChange("sms")}>
                <MessageSquareText className="mr-2 h-4 w-4" />
                SMS API
              </Button>
              <Button variant={type === "whatsapp" ? "default" : "outline"} className={type === "whatsapp" ? "bg-orange-500 hover:bg-orange-600" : ""} onClick={() => handleTypeChange("whatsapp")}>
                <Globe2 className="mr-2 h-4 w-4" />
                WhatsApp API
              </Button>
              <Button variant={type === "kyc" ? "default" : "outline"} className={type === "kyc" ? "bg-orange-500 hover:bg-orange-600" : ""} onClick={() => handleTypeChange("kyc")}>
                <BookOpenText className="mr-2 h-4 w-4" />
                KYC API
              </Button>
              <Button variant="outline" onClick={() => copyText(`${window.location.origin}${docMeta.html}`, "Viewer link")}>
                <Copy className="mr-2 h-4 w-4" />
                Copy Link
              </Button>
              <Button variant="outline" onClick={() => window.open(docMeta.markdown, "_blank", "noopener,noreferrer")}>
                <FileCode2 className="mr-2 h-4 w-4" />
                Markdown
              </Button>
              <Button variant="outline" onClick={() => window.open(docMeta.html, "_blank", "noopener,noreferrer")}>
                <ExternalLink className="mr-2 h-4 w-4" />
                Open Full Page
              </Button>
            </div>
          </div>
        </DialogHeader>
        <div className="grid h-[82vh] grid-cols-1 bg-slate-50 xl:grid-cols-[300px_minmax(0,1fr)]">
          <aside className="hidden border-r border-slate-200 bg-white xl:flex xl:flex-col">
            <div className="border-b border-slate-200 px-5 py-4">
              <p className="text-xs font-semibold uppercase tracking-[0.16em] text-slate-500">Sections</p>
              <p className="mt-1 text-sm text-slate-600">Browse endpoints, auth rules, request examples, and operational notes.</p>
            </div>
            <div className="flex-1 space-y-1 overflow-y-auto px-3 py-3">
              {sections.length ? sections.map((section) => (
                <button
                  key={section.id}
                  type="button"
                  onClick={() => jumpToSection(section.id)}
                  className={`flex w-full items-center rounded-xl px-3 py-2 text-left text-sm transition ${
                    activeSection === section.id
                      ? "bg-orange-50 text-orange-700 shadow-sm"
                      : "text-slate-600 hover:bg-slate-50 hover:text-slate-900"
                  } ${section.level === 3 ? "ml-4" : ""}`}
                >
                  {section.label}
                </button>
              )) : (
                <div className="px-3 py-6 text-sm text-slate-500">{loading ? "Loading documentation..." : "No sections available."}</div>
              )}
            </div>
          </aside>
          <div className="relative h-full overflow-hidden bg-slate-100">
            {loading ? (
              <div className="absolute inset-0 z-10 flex items-center justify-center bg-slate-50/80 text-sm text-slate-500">
                Loading documentation...
              </div>
            ) : null}
            <div className="border-b border-slate-200 bg-white px-4 py-3 xl:hidden">
              <p className="text-xs font-semibold uppercase tracking-[0.16em] text-slate-500">Quick Jump</p>
              <div className="mt-3 flex gap-2 overflow-x-auto pb-1">
                {sections.length ? sections.map((section) => (
                  <button
                    key={section.id}
                    type="button"
                    onClick={() => jumpToSection(section.id)}
                    className={`shrink-0 rounded-full border px-3 py-1.5 text-xs font-medium transition ${
                      activeSection === section.id
                        ? "border-orange-200 bg-orange-50 text-orange-700"
                        : "border-slate-200 bg-white text-slate-600"
                    }`}
                  >
                    {section.label}
                  </button>
                )) : (
                  <span className="text-sm text-slate-500">{loading ? "Loading sections..." : "No sections available."}</span>
                )}
              </div>
            </div>
            <div
              ref={contentRef}
              className="h-full overflow-y-auto bg-white"
              dangerouslySetInnerHTML={{ __html: `<div class="textzy-doc-surface">${htmlContent}</div>` }}
            />
          </div>
        </div>
      </DialogContent>
    </Dialog>
  );
}
