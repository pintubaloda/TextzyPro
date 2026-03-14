import { useEffect, useMemo, useState } from 'react'
import * as signalR from '@microsoft/signalr'
import { apiGet, apiPost, hasPermission } from '../../../api/client'
import { useAuth } from '../../../auth/AuthProvider'
import { useToast } from '../../../feedback/ToastProvider'
import WabaShell from '../../../components/waba/WabaShell'

export default function WabaLiveChatPage() {
  const { session } = useAuth()
  const toast = useToast()
  const canSend = useMemo(() => hasPermission('inbox.write', session), [session])
  const [conversations, setConversations] = useState([])
  const [activeId, setActiveId] = useState('')
  const [notes, setNotes] = useState([])
  const [messages, setMessages] = useState([])
  const [input, setInput] = useState('')
  const [noteInput, setNoteInput] = useState('')
  const buildIdempotencyKey = (prefix = 'waba-live') => `${prefix}-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`

  async function loadInbox() {
    const [conv, msg] = await Promise.all([apiGet('/api/inbox/conversations'), apiGet('/api/messages')])
    setConversations(conv)
    setMessages(msg)
    if (!activeId && conv.length) setActiveId(conv[0].id)
  }

  async function loadNotes(conversationId) {
    if (!conversationId) return
    const items = await apiGet(`/api/inbox/conversations/${conversationId}/notes`)
    setNotes(items)
  }

  useEffect(() => { loadInbox().catch(() => toast.error('Load inbox failed')) }, [])
  useEffect(() => { loadNotes(activeId).catch(() => setNotes([])) }, [activeId])

  useEffect(() => {
    const runtimeConfig = typeof window !== 'undefined' ? (window.__APP_CONFIG__ || {}) : {}
    const baseUrl =
      runtimeConfig.API_BASE ||
      process.env.REACT_APP_API_BASE ||
      import.meta.env.VITE_API_BASE ||
      'https://textzy-backend-production.up.railway.app'
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`${baseUrl}/hubs/inbox`, {
        withCredentials: true,
        // Prefer WebSockets (lowest latency). Keep SSE/LongPolling as fallbacks for restrictive networks.
        transport:
          signalR.HttpTransportType.WebSockets |
          signalR.HttpTransportType.ServerSentEvents |
          signalR.HttpTransportType.LongPolling,
      })
      .withAutomaticReconnect()
      .build()

    connection.start()
      .then(() => {
        if (session.tenantSlug) {
          return connection.invoke('JoinTenantRoom', session.tenantSlug)
        }
        return null
      })
      .catch(() => {})

    connection.on('message.queued', () => loadInbox().catch(() => {}))
    connection.on('message.sent', () => loadInbox().catch(() => {}))
    connection.on('conversation.assigned', () => loadInbox().catch(() => {}))
    connection.on('conversation.note', (n) => {
      if (n?.conversationId === activeId) setNotes((prev) => [n, ...prev])
    })

    return () => {
      if (session.tenantSlug) {
        connection.invoke('LeaveTenantRoom', session.tenantSlug).catch(() => {})
      }
      connection.stop().catch(() => {})
    }
  }, [session.tenantSlug, activeId])

  async function send() {
    if (!input.trim()) return
    const body = input
    setInput('')
    try {
      await apiPost('/api/messages/send', { recipient: '+910000000000', body, channel: 2, idempotencyKey: buildIdempotencyKey() })
      toast.success('Message sent')
    } catch {
      setInput(body)
      toast.error('Message send failed')
    }
  }

  async function sendTyping(isTyping) {
    if (!activeId) return
    try { await apiPost('/api/inbox/typing', { conversationId: activeId, isTyping }) } catch {}
  }

  async function assignToMe() {
    if (!activeId) return
    try {
      await apiPost(`/api/inbox/conversations/${activeId}/assign`, { userId: session.email || 'current-user', userName: session.email || 'Current User' })
      toast.success('Assigned')
      loadInbox()
    } catch {
      toast.error('Assign failed')
    }
  }

  async function addNote() {
    if (!activeId || !noteInput.trim()) return
    const body = noteInput
    setNoteInput('')
    try {
      await apiPost(`/api/inbox/conversations/${activeId}/notes`, { body })
      toast.success('Note added')
      loadNotes(activeId)
    } catch {
      setNoteInput(body)
      toast.error('Add note failed')
    }
  }

  return (
    <WabaShell>
      <div className="templates-head"><h1>Live Chat</h1></div>
      <section className="panel dark contact-grid">
        <div>
          <h3>Conversations</h3>
          <div className="chat-box">
            {conversations.map((c) => (
              <div key={c.id} className={`msg ${activeId === c.id ? 'agent' : 'customer'}`} onClick={() => setActiveId(c.id)}>
                <strong>{c.customerName || c.customerPhone}</strong>
                <div>{c.assignedUserName || 'Unassigned'}</div>
              </div>
            ))}
          </div>
          <button className="ghost" onClick={assignToMe}>Assign To Me</button>
        </div>
        <div>
          <h3>Messages</h3>
          <div className="chat-box">
            {messages.slice(0, 30).map((m) => <div key={m.id} className='msg agent'><strong>Agent: </strong>{m.body}</div>)}
          </div>
          <div className="chat-input-row">
            <input value={input} onFocus={() => sendTyping(true)} onBlur={() => sendTyping(false)} onChange={(e) => setInput(e.target.value)} placeholder="Type message" disabled={!canSend} />
            <button className="primary" onClick={send} disabled={!canSend}>Send</button>
          </div>

          <h3 style={{ marginTop: 14 }}>Internal Notes</h3>
          <div className="chat-box" style={{ minHeight: 120 }}>
            {notes.map((n) => <div key={n.id} className="msg customer"><strong>{n.createdByName}:</strong> {n.body}</div>)}
          </div>
          <div className="chat-input-row">
            <input value={noteInput} onChange={(e) => setNoteInput(e.target.value)} placeholder="Add internal note" />
            <button className="ghost" onClick={addNote}>Add</button>
          </div>
        </div>
      </section>
    </WabaShell>
  )
}
