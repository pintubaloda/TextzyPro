import { useCallback, useMemo, useRef, useState } from "react";
import { BookOpenText, Copy, ExternalLink, FileCode2, Hash, MessageSquareText } from "lucide-react";
import { toast } from "sonner";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";

const DOCS = {
  sms: {
    title: "SMS API Reference",
    description: "Public SMS integration reference with tenant-scoped credentials, DLT rules, sender registry, and delivery reporting.",
    html: "/docs/sms-api-reference.html",
    markdown: "/docs/SMS_API_REFERENCE.md",
    tone: "from-orange-50 via-white to-amber-50",
    chip: "SMS",
  },
  whatsapp: {
    title: "WhatsApp API Reference",
    description: "Public and tenant-authenticated WhatsApp messaging reference with send samples, templates, media, webhooks, and flows.",
    html: "/docs/whatsapp-api-reference.html",
    markdown: "/docs/WHATSAPP_API_REFERENCE.md",
    tone: "from-cyan-50 via-white to-emerald-50",
    chip: "WhatsApp",
  },
};

function getDoc(type) {
  return DOCS[type] || DOCS.sms;
}

async function copyText(text, label) {
  await navigator.clipboard.writeText(text);
  toast.success(`${label} copied`);
}

export default function ApiDocsViewer({ open, onOpenChange, type, onTypeChange }) {
  const iframeRef = useRef(null);
  const [sections, setSections] = useState([]);
  const [activeSection, setActiveSection] = useState("");
  const [loaded, setLoaded] = useState(false);
  const docMeta = useMemo(() => getDoc(type), [type]);

  const enhanceIframeDocument = useCallback((doc) => {
    if (!doc) return;

    if (!doc.getElementById("textzy-doc-viewer-style")) {
      const style = doc.createElement("style");
      style.id = "textzy-doc-viewer-style";
      style.textContent = `
        .textzy-pre-wrap { position: relative; margin-top: 12px; }
        .textzy-copy-btn {
          position: absolute;
          top: 10px;
          right: 10px;
          z-index: 4;
          border: 1px solid rgba(226,232,240,0.9);
          background: rgba(255,255,255,0.96);
          color: #0f172a;
          border-radius: 10px;
          font-size: 12px;
          font-weight: 700;
          padding: 6px 10px;
          cursor: pointer;
          box-shadow: 0 8px 18px rgba(15,23,42,0.08);
        }
        .textzy-copy-btn:hover { background: #fff7ed; color: #c2410c; }
        .textzy-anchor-target { scroll-margin-top: 24px; }
      `;
      doc.head.appendChild(style);
    }

    Array.from(doc.querySelectorAll("h2, h3")).forEach((heading, index) => {
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

    Array.from(doc.querySelectorAll("pre")).forEach((pre, index) => {
      if (pre.closest(".textzy-pre-wrap")) return;
      const wrapper = doc.createElement("div");
      wrapper.className = "textzy-pre-wrap";
      pre.parentNode.insertBefore(wrapper, pre);
      wrapper.appendChild(pre);

      const button = doc.createElement("button");
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

    const headingNodes = Array.from(doc.querySelectorAll("h2, h3")).map((heading, index) => ({
      id: heading.id || `section-${index}`,
      label: (heading.textContent || `Section ${index + 1}`).trim(),
      level: heading.tagName.toLowerCase() === "h2" ? 2 : 3,
    }));
    setSections(headingNodes);
    setActiveSection((prev) => prev || headingNodes[0]?.id || "");

    const observer = new doc.defaultView.IntersectionObserver(
      (entries) => {
        const visible = entries
          .filter((entry) => entry.isIntersecting)
          .sort((a, b) => a.boundingClientRect.top - b.boundingClientRect.top)[0];
        if (visible?.target?.id) setActiveSection(visible.target.id);
      },
      { rootMargin: "-10% 0px -70% 0px", threshold: [0, 1] }
    );

    headingNodes.forEach((item) => {
      const el = doc.getElementById(item.id);
      if (el) observer.observe(el);
    });
  }, []);

  const handleLoad = useCallback(() => {
    const iframe = iframeRef.current;
    const doc = iframe?.contentDocument;
    setLoaded(true);
    enhanceIframeDocument(doc);
  }, [enhanceIframeDocument]);

  const jumpToSection = useCallback((id) => {
    const doc = iframeRef.current?.contentDocument;
    const target = doc?.getElementById(id);
    if (target) {
      target.scrollIntoView({ behavior: "smooth", block: "start" });
      setActiveSection(id);
    }
  }, []);

  const handleTypeChange = (nextType) => {
    setLoaded(false);
    setSections([]);
    setActiveSection("");
    onTypeChange(nextType);
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-[95vw] overflow-hidden border-slate-200 p-0 sm:max-w-[95vw]">
        <DialogHeader className={`border-b border-slate-200 bg-gradient-to-r ${docMeta.tone} px-6 py-5`}>
          <div className="flex flex-col gap-4 xl:flex-row xl:items-start xl:justify-between">
            <div className="space-y-2">
              <div className="flex items-center gap-2">
                <Badge className="rounded-full bg-slate-900 px-3 py-1 text-white hover:bg-slate-900">
                  <BookOpenText className="mr-1.5 h-3.5 w-3.5" />
                  {docMeta.chip}
                </Badge>
                <Badge variant="outline" className="rounded-full border-slate-300 bg-white/80 text-slate-700">
                  In-app documentation
                </Badge>
              </div>
              <DialogTitle className="text-2xl font-bold text-slate-950">{docMeta.title}</DialogTitle>
              <DialogDescription className="max-w-3xl text-sm text-slate-600">
                {docMeta.description}
              </DialogDescription>
            </div>
            <div className="flex flex-wrap gap-2">
              <Button variant={type === "sms" ? "default" : "outline"} className={type === "sms" ? "bg-orange-500 hover:bg-orange-600" : ""} onClick={() => handleTypeChange("sms")}>
                <MessageSquareText className="mr-2 h-4 w-4" />
                SMS API
              </Button>
              <Button variant={type === "whatsapp" ? "default" : "outline"} className={type === "whatsapp" ? "bg-orange-500 hover:bg-orange-600" : ""} onClick={() => handleTypeChange("whatsapp")}>
                <Hash className="mr-2 h-4 w-4" />
                WhatsApp API
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
        <div className="grid h-[82vh] grid-cols-1 bg-slate-50 xl:grid-cols-[280px_minmax(0,1fr)]">
          <aside className="hidden border-r border-slate-200 bg-white xl:flex xl:flex-col">
            <div className="border-b border-slate-200 px-5 py-4">
              <p className="text-xs font-semibold uppercase tracking-[0.16em] text-slate-500">Sections</p>
              <p className="mt-1 text-sm text-slate-600">Jump directly to the relevant part of the reference.</p>
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
                <div className="px-3 py-6 text-sm text-slate-500">Loading section navigation…</div>
              )}
            </div>
          </aside>
          <div className="relative h-full bg-slate-100">
            {!loaded ? (
              <div className="absolute inset-0 z-10 flex items-center justify-center bg-slate-50/80 text-sm text-slate-500">
                Loading documentation…
              </div>
            ) : null}
            <iframe
              ref={iframeRef}
              title={docMeta.title}
              src={docMeta.html}
              onLoad={handleLoad}
              className="h-full w-full border-0 bg-white"
            />
          </div>
        </div>
      </DialogContent>
    </Dialog>
  );
}
