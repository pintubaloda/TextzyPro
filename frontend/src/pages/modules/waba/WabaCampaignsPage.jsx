import { useEffect, useMemo, useState } from 'react'
import { apiGet, apiPost, apiRequest, hasPermission } from '../../../api/client'
import { useAuth } from '../../../auth/AuthProvider'
import { useToast } from '../../../feedback/ToastProvider'
import WabaShell from '../../../components/waba/WabaShell'

export default function WabaCampaignsPage() {
  const { session } = useAuth()
  const toast = useToast()
  const canWrite = useMemo(() => hasPermission('campaigns.write', session), [session])
  const [items, setItems] = useState([])
  const [draft, setDraft] = useState({ name: '', channel: 2, templateText: '' })
  const [editingId, setEditingId] = useState('')
  const [editDraft, setEditDraft] = useState({ name: '', templateText: '' })
  const [broadcasts, setBroadcasts] = useState([])

  async function load() {
    const [c, b] = await Promise.all([apiGet('/api/campaigns'), apiGet('/api/broadcasts')])
    setItems(c)
    setBroadcasts(b)
  }
  useEffect(() => { load() }, [])

  async function createItem() {
    const optimistic = { id: `tmp-${Date.now()}`, ...draft }
    const prev = items
    setItems([optimistic, ...items])
    try { await apiPost('/api/campaigns', { ...draft, channel: Number(draft.channel) }); setDraft({ name: '', channel: 2, templateText: '' }); toast.success('Campaign created'); load() }
    catch { setItems(prev); toast.error('Campaign create failed') }
  }

  async function updateItem(id) {
    const prev = items
    setItems(items.map((x) => x.id === id ? { ...x, ...editDraft } : x))
    try {
      const current = prev.find((x) => x.id === id)
      const res = await apiRequest(`/api/campaigns/${id}`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ ...current, ...editDraft }) })
      if (!res.ok) throw new Error('update failed')
      setEditingId('')
      toast.success('Campaign updated')
    } catch { setItems(prev); toast.error('Campaign update failed') }
  }

  async function removeItem(id) {
    const prev = items
    setItems(items.filter((x) => x.id !== id))
    try { const res = await apiRequest(`/api/campaigns/${id}`, { method: 'DELETE' }); if (!res.ok) throw new Error(); toast.success('Campaign deleted') }
    catch { setItems(prev); toast.error('Campaign delete failed') }
  }

  async function enqueueBroadcast(item) {
    try {
      await apiPost('/api/broadcasts', {
        name: `${item.name} Broadcast`,
        channel: Number(item.channel),
        messageBody: item.templateText || 'Campaign broadcast',
        recipients: ['+910000000000']
      })
      toast.success('Broadcast queued')
      load()
    } catch {
      toast.error('Broadcast queue failed')
    }
  }

  return <WabaShell><div className="templates-head"><h1>Campaigns</h1>{canWrite && <button className="primary" onClick={createItem}>Create Campaign</button>}</div>{canWrite && <section className="panel dark" style={{ marginBottom: 12 }}><div className="campaign-form"><input placeholder="Campaign name" value={draft.name} onChange={(e) => setDraft({ ...draft, name: e.target.value })} /><textarea rows={3} placeholder="Template text" value={draft.templateText} onChange={(e) => setDraft({ ...draft, templateText: e.target.value })} /></div></section>}<section className="panel dark"><table className="tpl-table"><thead><tr><th>Name</th><th>Channel</th><th>Template</th><th /></tr></thead><tbody>{items.map((x) => <tr key={x.id}><td>{editingId===x.id?<input value={editDraft.name} onChange={(e)=>setEditDraft({...editDraft,name:e.target.value})}/>:x.name}</td><td>{x.channel}</td><td>{editingId===x.id?<input value={editDraft.templateText} onChange={(e)=>setEditDraft({...editDraft,templateText:e.target.value})}/>:x.templateText}</td><td>{canWrite && (editingId===x.id? <><button className="primary" onClick={()=>updateItem(x.id)}>Save</button><button className="ghost" onClick={()=>setEditingId('')}>Cancel</button></> : <><button className="ghost" onClick={()=>{setEditingId(x.id);setEditDraft({name:x.name,templateText:x.templateText||''})}}>Edit</button><button className="ghost" onClick={() => enqueueBroadcast(x)}>Broadcast</button><button className="ghost" onClick={() => removeItem(x.id)}>Delete</button></>)}</td></tr>)}</tbody></table></section><section className="panel dark" style={{ marginTop: 12 }}><h3>Broadcast Queue</h3><table className="tpl-table"><thead><tr><th>Name</th><th>Status</th><th>Sent</th><th>Failed</th></tr></thead><tbody>{broadcasts.map((b)=><tr key={b.id}><td>{b.name}</td><td>{b.status}</td><td>{b.sentCount}</td><td>{b.failedCount}</td></tr>)}</tbody></table></section></WabaShell>
}
