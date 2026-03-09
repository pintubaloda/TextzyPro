export default function ChatComposer({
  C,
  I,
  input,
  onInputTyping,
  onSend,
  onMic,
  busySend,
  onEmoji,
  onAttach,
  inputRef,
  fileInputRef,
  onAttachmentSelected,
  setShowEmojiPicker,
}) {
  return (
    <div
      style={{
        background: "#fff",
        padding: "10px 12px 10px",
        display: "flex",
        alignItems: "center",
        gap: 8,
        flexShrink: 0,
        borderTop: `1px solid ${C.divider}`,
        boxShadow: "0 -2px 12px rgba(0,0,0,0.06)",
        paddingBottom: "calc(10px + env(safe-area-inset-bottom,0px))",
      }}
    >
      <button onClick={onEmoji} style={{ background: "none", border: "none", color: C.iconColor, cursor: "pointer", padding: "6px", display: "flex", borderRadius: "50%" }}>
        <I.Emoji />
      </button>
      <button onClick={onAttach} style={{ background: "none", border: "none", color: C.iconColor, cursor: "pointer", padding: "6px", display: "flex", borderRadius: "50%" }}>
        <I.Attach />
      </button>
      <div
        style={{ flex: 1, background: C.panelBg, borderRadius: 22, padding: "11px 16px", border: `1.5px solid ${C.divider}`, display: "flex", alignItems: "center", transition: "border-color 0.2s" }}
        onFocusCapture={(e) => (e.currentTarget.style.borderColor = C.orange)}
        onBlurCapture={(e) => (e.currentTarget.style.borderColor = C.divider)}
      >
        <input
          ref={inputRef}
          value={input}
          onChange={(e) => onInputTyping(e.target.value)}
          onFocus={() => setShowEmojiPicker(false)}
          onKeyDown={(e) => e.key === "Enter" && !e.shiftKey && onSend()}
          placeholder="Type a message..."
          style={{ border: "none", outline: "none", flex: 1, fontSize: 15, color: C.textMain, background: "transparent", fontFamily: "inherit" }}
        />
      </div>
      <button
        disabled={busySend}
        onClick={input.trim() ? onSend : onMic}
        style={{
          width: 48,
          height: 48,
          borderRadius: "50%",
          border: "none",
          flexShrink: 0,
          background: input.trim() ? `linear-gradient(135deg,${C.orange},${C.orangeLight})` : C.divider,
          color: input.trim() ? "#fff" : C.textSub,
          cursor: busySend ? "not-allowed" : "pointer",
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          boxShadow: input.trim() ? `0 4px 16px ${C.orange}55` : "none",
          transition: "all 0.2s",
          opacity: busySend ? 0.8 : 1,
        }}
      >
        {busySend ? (
          <span style={{ width: 16, height: 16, border: "2px solid rgba(255,255,255,0.7)", borderTopColor: "transparent", borderRadius: "50%", animation: "spin 0.8s linear infinite" }} />
        ) : input.trim() ? (
          <I.Send />
        ) : (
          <I.Mic />
        )}
      </button>
      <input ref={fileInputRef} type="file" style={{ display: "none" }} onChange={onAttachmentSelected} />
    </div>
  );
}
