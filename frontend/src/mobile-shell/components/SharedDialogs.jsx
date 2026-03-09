import ModalShell from "./ModalShell";
import { NoticeDialog, UpdateDialog } from "./Dialogs";

export default function SharedDialogs(props) {
  const {
    C,
    notice,
    setNotice,
    updatePrompt,
    setUpdatePrompt,
    openTrustedDownloadUrl,
    showNewChat,
    newChatRecipient,
    setNewChatRecipient,
    newChatBody,
    setNewChatBody,
    setShowNewChat,
    busy,
    submitNewChat,
    showTransfer,
    transferMode,
    transferAssignee,
    setTransferAssignee,
    teamMembers,
    setShowTransfer,
    submitTransferConversation,
    showLabelsModal,
    labelsInput,
    setLabelsInput,
    setShowLabelsModal,
    submitLabels,
    showNotesModal,
    notesInput,
    setNotesInput,
    setShowNotesModal,
    submitNote,
    notes,
    showTemplateModal,
    templates,
    selectedTemplateId,
    setSelectedTemplateId,
    setTemplateVars,
    templateParamIndexes,
    templateVars,
    setShowTemplateModal,
    sendTemplateFallback,
    showQaModal,
    QA_LIBRARY,
    setInput,
    setShowQaModal,
    inputRef,
    showDevicesModal,
    devices,
    revokeDevice,
    openDevicesModal,
    setShowDevicesModal,
    showSettingsModal,
    settingsCompact,
    setSettingsCompact,
    settingsSound,
    setSettingsSound,
    setShowSettingsModal,
    showNotificationsModal,
    notifEnabled,
    setNotifEnabled,
    setShowNotificationsModal,
  } = props;

  return (
    <>
      <NoticeDialog C={C} notice={notice} onClose={() => setNotice("")} />
      <UpdateDialog
        C={C}
        prompt={updatePrompt}
        onLater={() => setUpdatePrompt(null)}
        onUpdate={() => openTrustedDownloadUrl(updatePrompt?.downloadUrl)}
      />

      {showNewChat ? (
        <ModalShell>
          <h3 style={{ margin:"0 0 10px", fontSize:18, color:C.textMain }}>Start New Chat</h3>
          <label style={{ display:"block", fontSize:12, color:C.textSub, marginBottom:6 }}>WhatsApp Number</label>
          <input value={newChatRecipient} onChange={(e)=>setNewChatRecipient(e.target.value)} placeholder="+91xxxxxxxxxx" style={{ width:"100%", border:`1.5px solid ${C.divider}`, borderRadius:10, padding:"10px 12px", marginBottom:10 }} />
          <label style={{ display:"block", fontSize:12, color:C.textSub, marginBottom:6 }}>Start Message</label>
          <textarea value={newChatBody} onChange={(e)=>setNewChatBody(e.target.value)} rows={3} style={{ width:"100%", border:`1.5px solid ${C.divider}`, borderRadius:10, padding:"10px 12px", marginBottom:12, resize:"vertical" }} />
          <div style={{ display:"flex", justifyContent:"flex-end", gap:8 }}>
            <button onClick={()=>setShowNewChat(false)} style={{ border:`1px solid ${C.divider}`, borderRadius:10, padding:"9px 14px", background:"#fff", cursor:"pointer" }}>Cancel</button>
            <button disabled={busy.newChat} onClick={submitNewChat} style={{ border:"none", borderRadius:10, padding:"9px 14px", background:C.orange, color:"#fff", fontWeight:700, cursor:busy.newChat?"not-allowed":"pointer", opacity:busy.newChat?0.8:1 }}>{busy.newChat ? "Starting..." : "Start"}</button>
          </div>
        </ModalShell>
      ) : null}

      {showTransfer ? (
        <ModalShell>
          <h3 style={{ margin:"0 0 10px", fontSize:18, color:C.textMain }}>{transferMode === "assign" ? "Assign Chat" : "Transfer Chat"}</h3>
          <label style={{ display:"block", fontSize:12, color:C.textSub, marginBottom:6 }}>Assignee Name or Email</label>
          <input value={transferAssignee} onChange={(e)=>setTransferAssignee(e.target.value)} placeholder="Enter assignee" style={{ width:"100%", border:`1.5px solid ${C.divider}`, borderRadius:10, padding:"10px 12px", marginBottom:8 }} />
          {teamMembers.length > 0 ? (
            <div style={{ maxHeight:120, overflowY:"auto", border:`1px solid ${C.divider}`, borderRadius:10, marginBottom:12 }}>
              {teamMembers.slice(0, 10).map((m, idx) => {
                const label = m.fullName || m.name || m.email || `User ${idx+1}`;
                return <button key={`${label}-${idx}`} onClick={()=>setTransferAssignee(label)} style={{ width:"100%", textAlign:"left", border:"none", borderBottom: idx < Math.min(teamMembers.length,10)-1 ? `1px solid ${C.divider}` : "none", background:"#fff", padding:"8px 10px", cursor:"pointer" }}>{label}</button>;
              })}
            </div>
          ) : null}
          <div style={{ display:"flex", justifyContent:"flex-end", gap:8 }}>
            <button onClick={()=>setShowTransfer(false)} style={{ border:`1px solid ${C.divider}`, borderRadius:10, padding:"9px 14px", background:"#fff", cursor:"pointer" }}>Cancel</button>
            <button disabled={busy.transfer} onClick={submitTransferConversation} style={{ border:"none", borderRadius:10, padding:"9px 14px", background:C.orange, color:"#fff", fontWeight:700, cursor:busy.transfer?"not-allowed":"pointer", opacity:busy.transfer?0.8:1 }}>{busy.transfer ? (transferMode === "assign" ? "Assigning..." : "Transferring...") : (transferMode === "assign" ? "Assign" : "Transfer")}</button>
          </div>
        </ModalShell>
      ) : null}

      {showLabelsModal ? (
        <ModalShell>
          <h3 style={{ margin:"0 0 10px", fontSize:18, color:C.textMain }}>Set Labels</h3>
          <label style={{ display:"block", fontSize:12, color:C.textSub, marginBottom:6 }}>Comma separated labels</label>
          <input value={labelsInput} onChange={(e)=>setLabelsInput(e.target.value)} placeholder="support, priority-high" style={{ width:"100%", border:`1.5px solid ${C.divider}`, borderRadius:10, padding:"10px 12px", marginBottom:12 }} />
          <div style={{ display:"flex", justifyContent:"flex-end", gap:8 }}>
            <button onClick={()=>setShowLabelsModal(false)} style={{ border:`1px solid ${C.divider}`, borderRadius:10, padding:"9px 14px", background:"#fff", cursor:"pointer" }}>Cancel</button>
            <button disabled={busy.labels} onClick={submitLabels} style={{ border:"none", borderRadius:10, padding:"9px 14px", background:C.orange, color:"#fff", fontWeight:700, cursor:busy.labels?"not-allowed":"pointer", opacity:busy.labels?0.8:1 }}>{busy.labels ? "Saving..." : "Save"}</button>
          </div>
        </ModalShell>
      ) : null}

      {showNotesModal ? (
        <ModalShell>
          <h3 style={{ margin:"0 0 10px", fontSize:18, color:C.textMain }}>Conversation Notes</h3>
          <textarea
            value={notesInput}
            onChange={(e)=>setNotesInput(e.target.value)}
            rows={3}
            placeholder="Add internal note..."
            style={{ width:"100%", border:`1.5px solid ${C.divider}`, borderRadius:10, padding:"10px 12px", marginBottom:10, resize:"vertical" }}
          />
          <div style={{ display:"flex", justifyContent:"flex-end", gap:8, marginBottom:10 }}>
            <button onClick={()=>setShowNotesModal(false)} style={{ border:`1px solid ${C.divider}`, borderRadius:10, padding:"9px 14px", background:"#fff", cursor:"pointer" }}>Close</button>
            <button disabled={busy.notes} onClick={submitNote} style={{ border:"none", borderRadius:10, padding:"9px 14px", background:C.orange, color:"#fff", fontWeight:700, cursor:busy.notes?"not-allowed":"pointer", opacity:busy.notes?0.8:1 }}>{busy.notes ? "Saving..." : "Save Note"}</button>
          </div>
          <div style={{ maxHeight:180, overflowY:"auto", border:`1px solid ${C.divider}`, borderRadius:10 }}>
            {notes.length === 0 ? (
              <div style={{ padding:"12px", color:C.textSub, fontSize:13 }}>No notes yet.</div>
            ) : notes.map((n, idx) => (
              <div key={`${n.id || n.Id || idx}`} style={{ padding:"10px 12px", borderBottom: idx < notes.length - 1 ? `1px solid ${C.divider}` : "none" }}>
                <div style={{ color:C.textMain, fontSize:13, lineHeight:1.4 }}>{n.body || n.Body || ""}</div>
                <div style={{ color:C.textMuted, fontSize:11, marginTop:4 }}>{new Date(n.createdAtUtc || n.CreatedAtUtc || Date.now()).toLocaleString()}</div>
              </div>
            ))}
          </div>
        </ModalShell>
      ) : null}

      {showTemplateModal ? (
        <ModalShell>
          <h3 style={{ margin:"0 0 10px", fontSize:18, color:C.textMain }}>Send Template</h3>
          {templates.length === 0 ? (
            <p style={{ margin:"0 0 12px", color:C.textSub, fontSize:13 }}>No approved WhatsApp templates available.</p>
          ) : (
            <>
              <label style={{ display:"block", fontSize:12, color:C.textSub, marginBottom:6 }}>Template</label>
              <select value={selectedTemplateId} onChange={(e)=>{ setSelectedTemplateId(e.target.value); setTemplateVars({}); }} style={{ width:"100%", border:`1.5px solid ${C.divider}`, borderRadius:10, padding:"10px 12px", marginBottom:10 }}>
                {templates.map((t) => (
                  <option key={String(t.id || t.Id)} value={String(t.id || t.Id)}>{t.name || t.Name}</option>
                ))}
              </select>
              {templateParamIndexes.map((idx) => (
                <div key={`tpl-${idx}`} style={{ marginBottom:8 }}>
                  <label style={{ display:"block", fontSize:12, color:C.textSub, marginBottom:4 }}>Variable {idx}</label>
                  <input
                    value={templateVars[idx] || ""}
                    onChange={(e)=>setTemplateVars((p)=>({ ...p, [idx]: e.target.value }))}
                    placeholder={`Value for {{${idx}}}`}
                    style={{ width:"100%", border:`1.5px solid ${C.divider}`, borderRadius:10, padding:"9px 12px" }}
                  />
                </div>
              ))}
            </>
          )}
          <div style={{ display:"flex", justifyContent:"flex-end", gap:8, marginTop:12 }}>
            <button onClick={()=>setShowTemplateModal(false)} style={{ border:`1px solid ${C.divider}`, borderRadius:10, padding:"9px 14px", background:"#fff", cursor:"pointer" }}>Cancel</button>
            <button disabled={busy.template || templates.length === 0} onClick={sendTemplateFallback} style={{ border:"none", borderRadius:10, padding:"9px 14px", background:C.orange, color:"#fff", fontWeight:700, cursor:busy.template?"not-allowed":"pointer", opacity:(busy.template || templates.length === 0)?0.8:1 }}>{busy.template ? "Sending..." : "Send Template"}</button>
          </div>
        </ModalShell>
      ) : null}

      {showQaModal ? (
        <ModalShell>
          <h3 style={{ margin:"0 0 10px", fontSize:18, color:C.textMain }}>Select QA</h3>
          <div style={{ maxHeight:180, overflowY:"auto", border:`1px solid ${C.divider}`, borderRadius:10, marginBottom:12 }}>
            {QA_LIBRARY.map((q, i) => (
              <button key={i} onClick={()=>{ setInput((p)=> (p ? `${p}\n` : "") + q); setShowQaModal(false); setTimeout(()=>inputRef.current?.focus(),80); }} style={{ width:"100%", textAlign:"left", border:"none", borderBottom: i < QA_LIBRARY.length - 1 ? `1px solid ${C.divider}` : "none", background:"#fff", padding:"10px 12px", cursor:"pointer" }}>
                {q}
              </button>
            ))}
          </div>
          <div style={{ display:"flex", justifyContent:"flex-end" }}>
            <button onClick={()=>setShowQaModal(false)} style={{ border:`1px solid ${C.divider}`, borderRadius:10, padding:"9px 14px", background:"#fff", cursor:"pointer" }}>Close</button>
          </div>
        </ModalShell>
      ) : null}

      {showDevicesModal ? (
        <ModalShell>
          <h3 style={{ margin:"0 0 10px", fontSize:18, color:C.textMain }}>Linked Devices</h3>
          <div style={{ maxHeight:220, overflowY:"auto", border:`1px solid ${C.divider}`, borderRadius:10, marginBottom:12 }}>
            {devices.length === 0 ? (
              <div style={{ padding:"14px 12px", color:C.textSub, fontSize:14 }}>No linked devices found.</div>
            ) : devices.map((d, idx) => {
              const id = d.id || d.Id || d.deviceId || d.DeviceId;
              const name = d.deviceName || d.DeviceName || "Mobile Device";
              const platform = d.devicePlatform || d.DevicePlatform || "";
              const model = d.deviceModel || d.DeviceModel || "";
              const detail = [platform, model].filter(Boolean).join(" | ");
              return (
                <div key={`${id || idx}`} style={{ display:"flex", alignItems:"center", gap:10, padding:"10px 12px", borderBottom: idx < devices.length - 1 ? `1px solid ${C.divider}` : "none" }}>
                  <div style={{ flex:1, minWidth:0 }}>
                    <div style={{ color:C.textMain, fontWeight:600, fontSize:14 }}>{name}</div>
                    <div style={{ color:C.textSub, fontSize:12, whiteSpace:"nowrap", overflow:"hidden", textOverflow:"ellipsis" }}>{detail || "Active session"}</div>
                  </div>
                  <button disabled={busy.devices} onClick={()=>revokeDevice(id)} style={{ border:`1px solid ${C.danger}33`, background:"#fff", color:C.danger, borderRadius:9, padding:"7px 10px", fontWeight:600, cursor:busy.devices?"not-allowed":"pointer", opacity:busy.devices?0.8:1 }}>Revoke</button>
                </div>
              );
            })}
          </div>
          <div style={{ display:"flex", justifyContent:"space-between", gap:8 }}>
            <button disabled={busy.devices} onClick={openDevicesModal} style={{ border:`1px solid ${C.divider}`, borderRadius:10, padding:"9px 14px", background:"#fff", cursor:busy.devices?"not-allowed":"pointer" }}>{busy.devices ? "Refreshing..." : "Refresh"}</button>
            <button onClick={()=>setShowDevicesModal(false)} style={{ border:"none", borderRadius:10, padding:"9px 14px", background:C.orange, color:"#fff", fontWeight:700, cursor:"pointer" }}>Close</button>
          </div>
        </ModalShell>
      ) : null}

      {showSettingsModal ? (
        <ModalShell>
          <h3 style={{ margin:"0 0 12px", fontSize:18, color:C.textMain }}>Settings</h3>
          <div style={{ display:"grid", gap:10, marginBottom:14 }}>
            {[
              { label: "Compact chat layout", value: settingsCompact, setter: setSettingsCompact },
              { label: "Message send sound", value: settingsSound, setter: setSettingsSound },
            ].map((row) => (
              <label key={row.label} style={{ display:"flex", alignItems:"center", justifyContent:"space-between", padding:"10px 12px", border:`1px solid ${C.divider}`, borderRadius:10 }}>
                <span style={{ color:C.textMain, fontSize:14 }}>{row.label}</span>
                <input type="checkbox" checked={row.value} onChange={(e)=>row.setter(e.target.checked)} />
              </label>
            ))}
          </div>
          <div style={{ display:"flex", justifyContent:"flex-end" }}>
            <button onClick={()=>setShowSettingsModal(false)} style={{ border:"none", borderRadius:10, padding:"9px 14px", background:C.orange, color:"#fff", fontWeight:700, cursor:"pointer" }}>Done</button>
          </div>
        </ModalShell>
      ) : null}

      {showNotificationsModal ? (
        <ModalShell>
          <h3 style={{ margin:"0 0 12px", fontSize:18, color:C.textMain }}>Notifications</h3>
          <label style={{ display:"flex", alignItems:"center", justifyContent:"space-between", padding:"10px 12px", border:`1px solid ${C.divider}`, borderRadius:10, marginBottom:12 }}>
            <span style={{ color:C.textMain, fontSize:14 }}>Enable push notifications</span>
            <input type="checkbox" checked={notifEnabled} onChange={(e)=>setNotifEnabled(e.target.checked)} />
          </label>
          <p style={{ margin:"0 0 12px", color:C.textSub, fontSize:12 }}>
            Notification preferences are stored on this device.
          </p>
          <div style={{ display:"flex", justifyContent:"flex-end" }}>
            <button onClick={()=>setShowNotificationsModal(false)} style={{ border:"none", borderRadius:10, padding:"9px 14px", background:C.orange, color:"#fff", fontWeight:700, cursor:"pointer" }}>Done</button>
          </div>
        </ModalShell>
      ) : null}
    </>
  );
}

