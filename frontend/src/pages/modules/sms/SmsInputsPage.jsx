import { useEffect, useMemo, useState } from 'react'
import { apiGet, apiPost, apiRequest, hasPermission } from '../../../api/client'
import { useAuth } from '../../../auth/AuthProvider'
import { useToast } from '../../../feedback/ToastProvider'
import SmsShell from '../../../components/sms/SmsShell'

export default function SmsInputsPage() {
  const { session } = useAuth()
  const toast = useToast()
  const canWrite = useMemo(() => hasPermission('automation.write', session), [session])
  const [fields, setFields] = useState([])
  const [draft, setDraft] = useState({ name: '', type: 'text' })
  const [editingId, setEditingId] = useState('')
  const [editDraft, setEditDraft] = useState({ name: '', type: 'text' })

  async function load() { setFields(await apiGet('/api/sms/inputs')) }
  useEffect(() => { load() }, [])

  async function addField() { const optimistic = { id: `tmp-${Date.now()}`, ...draft }; const prev = fields; setFields([...fields, optimistic]); try { await apiPost('/api/sms/inputs', draft); setDraft({ name: '', type: 'text' }); toast.success('Field added'); load() } catch { setFields(prev); toast.error('Field add failed') } }

  async function update(id) {
    const prev = fields
    setFields(fields.map((f) => f.id === id ? { ...f, ...editDraft } : f))
    try {
      const current = prev.find((x) => x.id === id)
      const res = await apiRequest(`/api/sms/inputs/${id}`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ ...current, ...editDraft }) })
      if (!res.ok) throw new Error()
      setEditingId('')
      toast.success('Field updated')
    } catch { setFields(prev); toast.error('Field update failed') }
  }

  async function remove(id) { const prev = fields; setFields(fields.filter((x) => x.id !== id)); try { const res = await apiRequest(`/api/sms/inputs/${id}`, { method: 'DELETE' }); if (!res.ok) throw new Error(); toast.success('Field deleted') } catch { setFields(prev); toast.error('Field delete failed') } }

  return <SmsShell><div className="templates-head"><h1>Input Fields</h1></div><section className="panel dark contact-grid"><div><h3>Add Field</h3><div className="campaign-form"><label>Field Name</label><input value={draft.name} onChange={(e) => setDraft({ ...draft, name: e.target.value })} /><label>Field Type</label><select value={draft.type} onChange={(e) => setDraft({ ...draft, type: e.target.value })}><option value="text">Text</option><option value="number">Number</option><option value="date">Date</option></select></div>{canWrite && <div className="actions"><button className="primary" onClick={addField}>Add Field</button></div>}</div><div><h3>Available Fields</h3><table className="tpl-table"><thead><tr><th>Name</th><th>Type</th><th /></tr></thead><tbody>{fields.map((f) => <tr key={f.id}><td>{editingId===f.id?<input value={editDraft.name} onChange={(e)=>setEditDraft({...editDraft,name:e.target.value})}/>:f.name}</td><td>{editingId===f.id?<select value={editDraft.type} onChange={(e)=>setEditDraft({...editDraft,type:e.target.value})}><option value='text'>Text</option><option value='number'>Number</option><option value='date'>Date</option></select>:f.type}</td><td>{canWrite && (editingId===f.id? <><button className='primary' onClick={()=>update(f.id)}>Save</button><button className='ghost' onClick={()=>setEditingId('')}>Cancel</button></> : <><button className='ghost' onClick={()=>{setEditingId(f.id);setEditDraft({name:f.name,type:f.type})}}>Edit</button><button className='ghost' onClick={() => remove(f.id)}>Delete</button></>)}</td></tr>)}</tbody></table></div></section></SmsShell>
}
