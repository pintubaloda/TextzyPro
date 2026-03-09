import { useEffect, useMemo, useState } from 'react'
import { apiGet, apiPost, apiRequest, hasPermission } from '../../../api/client'
import { useAuth } from '../../../auth/AuthProvider'
import { useToast } from '../../../feedback/ToastProvider'
import WabaShell from '../../../components/waba/WabaShell'

export default function WabaTemplatesPage() {
  const { session } = useAuth()
  const toast = useToast()
  const canWrite = useMemo(() => hasPermission('templates.write', session), [session])
  const [rows, setRows] = useState([])
  const [draft, setDraft] = useState({ name: '', body: '', channel: 2, category: 'UTILITY', language: 'en' })
  const [editingId, setEditingId] = useState('')
  const [editDraft, setEditDraft] = useState({ name: '', body: '' })

  async function load() { setRows(await apiGet('/api/templates')) }
  useEffect(() => { load() }, [])

  async function createTemplate() {
    const optimistic = { id: `tmp-${Date.now()}`, ...draft, status: 'Pending' }
    const prev = rows
    setRows([optimistic, ...rows])
    try {
      await apiPost('/api/templates', { ...draft, channel: Number(draft.channel) })
      setDraft({ name: '', body: '', channel: 2, category: 'UTILITY', language: 'en' })
      toast.success('Template created')
      load()
    } catch { setRows(prev); toast.error('Template create failed') }
  }

  async function updateTemplate(id) {
    const prev = rows
    setRows(rows.map((r) => (r.id === id ? { ...r, ...editDraft } : r)))
    try {
      const current = prev.find((x) => x.id === id)
      const res = await apiRequest(`/api/templates/${id}`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ ...current, ...editDraft }) })
      if (!res.ok) throw new Error('update failed')
      setEditingId('')
      toast.success('Template updated')
    } catch { setRows(prev); toast.error('Template update failed') }
  }

  async function removeTemplate(id) {
    const prev = rows
    setRows(rows.filter((x) => x.id !== id))
    try {
      const res = await apiRequest(`/api/templates/${id}`, { method: 'DELETE' })
      if (!res.ok) throw new Error('delete failed')
      toast.success('Template deleted')
    } catch { setRows(prev); toast.error('Template delete failed') }
  }

  async function lifecycle(id, action) {
    try {
      await apiPost(`/api/template-lifecycle/${id}/${action}`, {})
      toast.success(`Template ${action} success`)
      load()
    } catch {
      toast.error(`Template ${action} failed`)
    }
  }

  return <WabaShell><div className="templates-head"><h1>Templates</h1>{canWrite && <button className="primary" onClick={createTemplate}>Add Template</button>}</div>{canWrite && <section className="panel dark" style={{ marginBottom: 12 }}><div className="campaign-form"><input placeholder="Name" value={draft.name} onChange={(e) => setDraft({ ...draft, name: e.target.value })} /><textarea rows={3} placeholder="Body" value={draft.body} onChange={(e) => setDraft({ ...draft, body: e.target.value })} /></div></section>}<section className="panel dark"><table className="tpl-table"><thead><tr><th>Name</th><th>Category</th><th>Language</th><th>Channel</th><th>Status</th><th /></tr></thead><tbody>{rows.map((r) => <tr key={r.id}><td>{editingId===r.id?<input value={editDraft.name} onChange={(e)=>setEditDraft({...editDraft,name:e.target.value})}/>:r.name}</td><td>{r.category}</td><td>{r.language}</td><td>{r.channel}</td><td><span className="badge ok">{r.lifecycleStatus || r.status}</span></td><td>{canWrite && (editingId===r.id? <><button className="primary" onClick={()=>updateTemplate(r.id)}>Save</button><button className="ghost" onClick={()=>setEditingId('')}>Cancel</button></> : <><button className="ghost" onClick={()=>{setEditingId(r.id);setEditDraft({name:r.name,body:r.body||''})}}>Edit</button><button className="ghost" onClick={() => lifecycle(r.id, 'submit')}>Submit</button><button className="ghost" onClick={() => lifecycle(r.id, 'approve')}>Approve</button><button className="ghost" onClick={() => lifecycle(r.id, 'reject')}>Reject</button><button className="ghost" onClick={() => lifecycle(r.id, 'disable')}>Disable</button><button className="ghost" onClick={() => lifecycle(r.id, 'version')}>New Ver</button><button className="ghost" onClick={() => removeTemplate(r.id)}>Delete</button></>)}</td></tr>)}</tbody></table></section></WabaShell>
}
