export default function ChatView(props) {
  const {
    C,
    I,
    active,
    setView,
    setShowChatMenu,
    showChatMenu,
    handleAssignConversation,
    handleTransferConversation,
    handleSetLabels,
    handleToggleStar,
    handleNotes,
    setShowTemplateModal,
    loadApprovedTemplates,
    setShowQaModal,
    renderMessageBody,
    openMessageMedia,
    inputRef,
    Typing,
    msgEnd,
    showEmojiPicker,
    EMOJI_SET,
    setInput,
    ChatComposerNode,
    sharedDialogsNode,
    Avatar,
  } = props;

  return (
    <div
      style={{
        height: "100vh",
        display: "flex",
        flexDirection: "column",
        fontFamily: "'Segoe UI',system-ui,sans-serif",
        background: C.chatBg,
      }}
    >
      <div
        style={{
          background:
            `radial-gradient(560px 200px at 50% -35%, rgba(255,255,255,0.12), transparent 60%), linear-gradient(135deg,${C.headerBg},#16304F)`,
          padding: "10px 12px 10px",
          display: "flex",
          alignItems: "center",
          gap: 10,
          flexShrink: 0,
          boxShadow: "0 2px 12px rgba(30,58,95,0.3)",
          paddingTop: "calc(10px + env(safe-area-inset-top,0px))",
          position: "relative",
        }}
      >
        <button
          onClick={() => setView("list")}
          style={{
            background: "rgba(255,255,255,0.12)",
            border: "none",
            color: "#fff",
            padding: "7px 9px",
            borderRadius: 10,
            cursor: "pointer",
            display: "flex",
            flexShrink: 0,
          }}
        >
          <I.Back />
        </button>
        <Avatar name={active.name} color={active.color} size={38} online={active.online} />
        <div style={{ flex: 1, minWidth: 0 }}>
          <div
            style={{
              color: "#fff",
              fontWeight: 700,
              fontSize: 15,
              whiteSpace: "nowrap",
              overflow: "hidden",
              textOverflow: "ellipsis",
            }}
          >
            {active.name}
          </div>
          <div style={{ fontSize: 12 }}>
            {active.typing ? (
              <span style={{ color: C.orangeLight, fontStyle: "italic" }}>typing...</span>
            ) : (
              <span style={{ color: "rgba(255,255,255,0.65)" }}>
                {active.online ? "Online" : "Last seen recently"}
              </span>
            )}
          </div>
        </div>
        <button style={{ background: "rgba(255,255,255,0.12)", border: "none", color: "#fff", padding: "8px", borderRadius: 10, cursor: "pointer", display: "flex" }}>
          <I.Phone />
        </button>
        <button style={{ background: "rgba(255,255,255,0.12)", border: "none", color: "#fff", padding: "8px", borderRadius: 10, cursor: "pointer", display: "flex" }}>
          <I.Video />
        </button>
        <button onClick={() => setShowChatMenu((v) => !v)} style={{ background: "rgba(255,255,255,0.12)", border: "none", color: "#fff", padding: "8px", borderRadius: 10, cursor: "pointer", display: "flex" }}>
          <I.More />
        </button>
      </div>
      {showChatMenu && (
        <div style={{ position: "absolute", top: "calc(10px + env(safe-area-inset-top,0px) + 58px)", right: 12, zIndex: 5, background: "#fff", borderRadius: 12, boxShadow: "0 14px 34px rgba(0,0,0,0.26)", overflow: "hidden", border:`1px solid ${C.divider}` }}>
          {[
            { label: "Assign", onClick: handleAssignConversation },
            { label: "Transfer", onClick: handleTransferConversation },
            { label: "Labels", onClick: handleSetLabels },
            { label: (active.labels || []).some((l) => String(l).toLowerCase() === "starred") ? "Unstar Chat" : "Star Chat", onClick: handleToggleStar },
            { label: "Notes", onClick: handleNotes },
            { label: "Send Template", onClick: async () => { setShowChatMenu(false); setShowTemplateModal(true); await loadApprovedTemplates().catch(() => {}); } },
            { label: "Insert QA", onClick: () => { setShowChatMenu(false); setShowQaModal(true); } },
          ].map((item) => (
            <button key={item.label} onClick={item.onClick} style={{ display: "block", width: "100%", textAlign: "left", background: "#fff", border: "none", padding: "11px 14px", fontSize: 14, cursor: "pointer", color:C.textMain }}>
              {item.label}
            </button>
          ))}
        </div>
      )}

      <div
        style={{
          flex: 1,
          overflowY: "auto",
          padding: "14px 14px 18px",
          background: C.chatBg,
          backgroundImage:
            `url("data:image/svg+xml,%3Csvg width='52' height='52' viewBox='0 0 52 52' xmlns='http://www.w3.org/2000/svg'%3E%3Cg fill='%23F97316' fill-opacity='0.04'%3E%3Cpath d='M10 10h10v10H10zm22 0h10v10H32zm0 22h10v10H32zM10 32h10v10H10z'/%3E%3C/g%3E%3C/svg%3E")`,
        }}
      >
        <div style={{ textAlign: "center", marginBottom: 14 }}>
          <span style={{ background: "rgba(255,255,255,0.9)", color: C.textSub, fontSize: 11, padding: "4px 14px", borderRadius: 20, boxShadow: "0 1px 4px rgba(0,0,0,0.08)", fontWeight: 500 }}>
            TODAY
          </span>
        </div>

        {active.messages.map((msg) => (
          <div key={msg.id} style={{ display: "flex", justifyContent: msg.sent ? "flex-end" : "flex-start", marginBottom: 5, animation: "fadeUp 0.18s ease-out" }}>
            <div
              style={{
                maxWidth: "78%",
                padding: "9px 13px 6px",
                background: msg.sent ? C.bubbleSent : C.bubbleRecv,
                borderRadius: msg.sent ? "16px 16px 3px 16px" : "16px 16px 16px 3px",
                boxShadow: msg.sent ? `0 2px 8px ${C.orange}22` : "0 1px 4px rgba(0,0,0,0.08)",
              }}
            >
              {renderMessageBody(msg)}
              {msg.messageType?.startsWith("media:") ? (
                <div style={{ marginTop: 8, border: `1px solid ${C.divider}`, borderRadius: 10, padding: "8px 10px", background: "#fff" }}>
                  <div style={{ fontSize: 12, color: C.textSub, marginBottom: 6 }}>
                    {String(msg.messageType).split(":")[1] || "media"} {msg.media?.fileName ? `- ${msg.media.fileName}` : ""}
                  </div>
                  {msg.media?.caption ? (
                    <div style={{ fontSize: 13, color: C.textMain, marginBottom: 6 }}>{msg.media.caption}</div>
                  ) : null}
                  <button onClick={() => openMessageMedia(msg)} style={{ border: `1px solid ${C.orange}55`, background: "#fff", color: C.orangeDark, borderRadius: 8, padding: "6px 10px", fontSize: 12, fontWeight: 600, cursor: "pointer" }}>
                    Open Media
                  </button>
                </div>
              ) : null}
              {Array.isArray(msg.interactiveButtons) && msg.interactiveButtons.length > 0 ? (
                <div style={{ marginTop: 8, display: "grid", gap: 6 }}>
                  {msg.interactiveButtons.map((btn, idx) => (
                    <button
                      key={`${msg.id}-btn-${idx}`}
                      onClick={() => { setInput(btn); setTimeout(() => inputRef.current?.focus(), 40); }}
                      style={{
                        height: 32,
                        borderRadius: 8,
                        border: "1px solid #BAE6FD",
                        background: "#F0F9FF",
                        color: "#0369A1",
                        fontSize: 12,
                        fontWeight: 600,
                        cursor: "pointer",
                      }}
                    >
                      Reply: {btn}
                    </button>
                  ))}
                </div>
              ) : null}
              <div style={{ display: "flex", justifyContent: "flex-end", alignItems: "center", gap: 3, marginTop: 4 }}>
                <span style={{ fontSize: 11, color: C.textMuted }}>{msg.time}</span>
                {msg.sent && (msg.status === "read" ? <I.DblChk /> : <I.Check />)}
              </div>
            </div>
          </div>
        ))}

        {active.typing && (
          <div style={{ display: "flex", justifyContent: "flex-start", marginBottom: 5 }}>
            <div style={{ background: C.bubbleRecv, borderRadius: "16px 16px 16px 3px", boxShadow: "0 1px 4px rgba(0,0,0,0.08)" }}>
              <Typing />
            </div>
          </div>
        )}
        <div ref={msgEnd} />
      </div>

      {showEmojiPicker && (
        <div style={{ background: "#fff", borderTop: `1px solid ${C.divider}`, padding: "10px 12px", display: "grid", gridTemplateColumns: "repeat(8, 1fr)", gap: 8, flexShrink: 0 }}>
          {EMOJI_SET.map((e) => (
            <button
              key={e}
              onClick={() => { setInput((p) => `${p}${e}`); setTimeout(() => inputRef.current?.focus(), 40); }}
              style={{
                border: `1px solid ${C.divider}`,
                background: "#fff",
                borderRadius: 10,
                height: 34,
                fontSize: 20,
                lineHeight: 1,
                cursor: "pointer",
              }}
            >
              {e}
            </button>
          ))}
        </div>
      )}

      {ChatComposerNode}

      <style>{`
        *{box-sizing:border-box;margin:0;padding:0;}
        ::-webkit-scrollbar{width:0;}
        @keyframes fadeUp{from{opacity:0;transform:translateY(5px)}to{opacity:1;transform:translateY(0)}}
        @keyframes tdot{0%,60%,100%{transform:translateY(0)}30%{transform:translateY(-5px)}}
        @keyframes spin{to{transform:rotate(360deg)}}
      `}</style>
      {sharedDialogsNode}
    </div>
  );
}
