import ModalShell from "./ModalShell";

export function NoticeDialog({ C, notice, onClose }) {
  if (!notice) return null;
  return (
    <ModalShell>
      <h3 style={{ margin: "0 0 10px", fontSize: 18, color: C.textMain }}>Textzy</h3>
      <p style={{ margin: "0 0 16px", color: C.textSub, lineHeight: 1.45, whiteSpace: "pre-wrap" }}>{notice}</p>
      <div style={{ display: "flex", justifyContent: "flex-end" }}>
        <button
          onClick={onClose}
          style={{ border: "none", borderRadius: 10, padding: "10px 14px", background: C.orange, color: "#fff", fontWeight: 700, cursor: "pointer" }}
        >
          OK
        </button>
      </div>
    </ModalShell>
  );
}

export function UpdateDialog({ C, prompt, onLater, onUpdate }) {
  if (!prompt) return null;
  return (
    <ModalShell>
      <h3 style={{ margin: "0 0 10px", fontSize: 18, color: C.textMain }}>Update Available</h3>
      <p style={{ margin: "0 0 8px", color: C.textSub, lineHeight: 1.45 }}>
        Current version: {prompt.appVersion}
        {"\n"}
        Latest version: {prompt.latestVersion || "latest"}
      </p>
      <p style={{ margin: "0 0 16px", color: C.textSub, lineHeight: 1.45 }}>
        {prompt.forceUpdate ? "This update is required to continue." : "A newer app version is available."}
      </p>
      <div style={{ display: "flex", justifyContent: "flex-end", gap: 8 }}>
        {!prompt.forceUpdate ? (
          <button
            onClick={onLater}
            style={{ border: `1px solid ${C.divider}`, borderRadius: 10, padding: "10px 14px", background: "#fff", cursor: "pointer" }}
          >
            Later
          </button>
        ) : null}
        <button
          onClick={onUpdate}
          style={{ border: "none", borderRadius: 10, padding: "10px 14px", background: C.orange, color: "#fff", fontWeight: 700, cursor: "pointer" }}
        >
          Update Now
        </button>
      </div>
    </ModalShell>
  );
}
