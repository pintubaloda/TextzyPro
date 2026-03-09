import { useEffect, useMemo, useState } from 'react'
import { apiGet, apiPost, apiRequest, hasPermission } from '../../../api/client'
import { useAuth } from '../../../auth/AuthProvider'
import { useToast } from '../../../feedback/ToastProvider'
import SmsShell from '../../../components/sms/SmsShell'

export default function SmsCustomizePage() {
  const { session } = useAuth()
  const toast = useToast()
  const canWrite = useMemo(() => hasPermission('templates.write', session), [session])
  const [templates, setTemplates] = useState([])
  const [draft, setDraft] = useState({ name: '', body: '' })
  const [editingId, setEditingId] = useState('')
  const [editDraft, setEditDraft] = useState({ name: '', body: '' })

  async function load() { const all = await apiGet('/api/templates'); setTemplates(all.filter((x) => x.channel === 1)) }
  useEffect(() => { load() }, [])

  async function create() { const optimistic = { id: `tmp-${Date.now()}`, name: draft.name, body: draft.body, channel: 1, category:'MARKETING', language:'en', status:'Pending' }; const prev = templates; setTemplates([optimistic, ...templates]); try { await apiPost('/api/templates', { name: draft.name, body: draft.body, channel: 1, category: 'MARKETING', language: 'en' }); setDraft({ name: '', body: '' }); toast.success('SMS template created'); load() } catch { setTemplates(prev); toast.error('SMS template create failed') } }

  async function update(id) {
    const prev = templates
    setTemplates(templates.map((t) => t.id === id ? { ...t, ...editDraft } : t))
    try {
      const current = prev.find((x) => x.id === id)
      const res = await apiRequest(`/api/templates/${id}`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ ...current, ...editDraft, channel: 1 }) })
      if (!res.ok) throw new Error()
      setEditingId('')
      toast.success('SMS template updated')
    } catch { setTemplates(prev); toast.error('SMS template update failed') }
  }

  async function remove(id) { const prev = templates; setTemplates(templates.filter((x) => x.id !== id)); try { const res = await apiRequest(`/api/templates/${id}`, { method: 'DELETE' }); if (!res.ok) throw new Error(); toast.success('SMS template deleted') } catch { setTemplates(prev); toast.error('SMS template delete failed') } }

  return <SmsShell><div className="templates-head"><h1>SMS Customization</h1>{canWrite && <button className="primary" onClick={create}>Save Template</button>}</div>{canWrite && <section className="panel dark" style={{ marginBottom: 12 }}><div className="campaign-form"><input placeholder='Template name' value={draft.name} onChange={(e) => setDraft({ ...draft, name: e.target.value })} /><textarea rows={3} placeholder='Template body' value={draft.body} onChange={(e) => setDraft({ ...draft, body: e.target.value })} /></div></section>}<section className="panel dark"><table className="tpl-table"><thead><tr><th>Name</th><th>Body</th><th /></tr></thead><tbody>{templates.map((t) => <tr key={t.id}><td>{editingId===t.id?<input value={editDraft.name} onChange={(e)=>setEditDraft({...editDraft,name:e.target.value})}/>:t.name}</td><td>{editingId===t.id?<input value={editDraft.body} onChange={(e)=>setEditDraft({...editDraft,body:e.target.value})}/>:t.body}</td><td>{canWrite && (editingId===t.id? <><button className='primary' onClick={()=>update(t.id)}>Save</button><button className='ghost' onClick={()=>setEditingId('')}>Cancel</button></> : <><button className='ghost' onClick={()=>{setEditingId(t.id);setEditDraft({name:t.name,body:t.body||''})}}>Edit</button><button className='ghost' onClick={() => remove(t.id)}>Delete</button></>)}</td></tr>)}</tbody></table></section></SmsShell>
}
