import { useState } from "react";
import { C } from "../core";
import { PROJECTS, Logo } from "../uiAssets";

const ProjectPicker = ({ projects, onSelect, loading = false }) => {
  const [sel, setSel] = useState(null);
  const rows = projects?.length ? projects : PROJECTS;
  const badgeFor = (p) => {
    const icon = String(p?.icon || "").trim();
    if (/^[A-Za-z0-9]{1,4}$/.test(icon)) return icon.toUpperCase();
    const src = String(p?.name || p?.slug || "PR");
    const initials = src
      .split(/[\s_-]+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((s) => s[0]?.toUpperCase() || "")
      .join("");
    return initials || "PR";
  };
  return (
    <div style={{
      minHeight: "100vh", display: "flex", flexDirection: "column",
      background: `radial-gradient(900px 360px at 50% -10%, rgba(255,255,255,0.18), transparent 60%), linear-gradient(165deg,${C.orange} 0%,#EA6C0A 30%,#C2560A 65%,#1E3A5F 100%)`,
      fontFamily: "'Segoe UI',system-ui,sans-serif",
    }}>
      <div style={{ flex: "0 0 20vh", display: "flex", flexDirection: "column", alignItems: "center", justifyContent: "center", padding: "12px 24px 6px", position:"relative" }}>
        <div style={{ position:"absolute", width:140, height:140, borderRadius:"50%", background:"rgba(255,255,255,0.12)", filter:"blur(2px)", top:"18%", left:"50%", transform:"translateX(-50%)" }}/>
        <Logo size={34} />
        <h2 style={{ margin: "10px 0 4px", fontSize: 23, fontWeight: 800, color: "#fff" }}>Select Workspace</h2>
        <p style={{ margin: 0, color: "rgba(255,255,255,0.7)", fontSize: 13 }}>Choose a project to continue</p>
      </div>
      <div style={{ flex: 1, minHeight: "80vh", background: "#fff", borderRadius: "28px 28px 0 0", padding: "22px 20px 38px", boxShadow: "0 -14px 46px rgba(0,0,0,0.16)" }}>
        <div style={{ display:"grid", gridTemplateColumns:"repeat(3,minmax(0,1fr))", gap:8, marginBottom:14 }}>
          {[
            { k:"Projects", v:String(rows.length) },
            { k:"Role", v:"Scoped" },
            { k:"Security", v:"CSRF" },
          ].map((x)=>(
            <div key={x.k} style={{ border:`1px solid ${C.divider}`, borderRadius:10, background:"#fff", padding:"8px 10px", textAlign:"center" }}>
              <div style={{ fontSize:11, color:C.textSub }}>{x.k}</div>
              <div style={{ fontSize:13, fontWeight:700, color:C.textMain, marginTop:2 }}>{x.v}</div>
            </div>
          ))}
        </div>
        {rows.map((p) => {
          const a = sel === p.slug;
          return (
            <div key={p.slug} onClick={() => setSel(p.slug)} style={{
              display: "flex", alignItems: "center", gap: 14, padding: "14px 16px",
              borderRadius: 14, marginBottom: 10, cursor: "pointer",
              border: `2px solid ${a ? C.orange : C.divider}`,
              background: a ? C.orangePale : "#fff", transition: "all 0.15s",
              boxShadow: a ? `0 2px 12px ${C.orange}22` : "none",
            }}>
              <div style={{ width: 46, height: 46, borderRadius: 12, background: a ? C.orangeLight2 : C.panelBg, display: "flex", alignItems: "center", justifyContent: "center", fontSize: 15, fontWeight: 800, letterSpacing: "0.4px", color: C.textMain, flexShrink: 0 }}>{badgeFor(p)}</div>
              <div style={{ flex: 1 }}>
                <div style={{ fontWeight: 700, fontSize: 15, color: C.textMain }}>{p.name}</div>
                <div style={{ fontSize: 12, color: C.textSub, marginTop: 2 }}>{p.role} | /{p.slug}</div>
              </div>
              {a ? (
                <div style={{ width: 24, height: 24, borderRadius: "50%", background: C.orange, display: "flex", alignItems: "center", justifyContent: "center", flexShrink: 0 }}>
                  <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="#fff" strokeWidth="3.5" strokeLinecap="round" strokeLinejoin="round"><polyline points="20 6 9 17 4 12" /></svg>
                </div>
              ) : null}
            </div>
          );
        })}
        <button disabled={!sel || loading} onClick={() => sel && onSelect(rows.find((p) => p.slug === sel))} style={{
          width: "100%", padding: 15, marginTop: 8, borderRadius: 14, border: "none",
          background: sel ? `linear-gradient(135deg,${C.orange},${C.orangeLight})` : C.divider,
          color: sel ? "#fff" : C.textMuted, fontWeight: 700, fontSize: 16,
          cursor: sel && !loading ? "pointer" : "not-allowed", fontFamily: "inherit",
          boxShadow: sel ? `0 6px 24px ${C.orange}55` : "none", transition: "all 0.2s",
          opacity: loading ? 0.82 : 1,
        }}>{loading ? "Continuing..." : "Continue"}</button>
      </div>
    </div>
  );
};

export default ProjectPicker;
