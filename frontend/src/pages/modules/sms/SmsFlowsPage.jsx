import { useEffect, useMemo, useState } from 'react'
import { apiGet, apiPost, apiRequest, hasPermission } from '../../../api/client'
import { useAuth } from '../../../auth/AuthProvider'
import { useToast } from '../../../feedback/ToastProvider'
import SmsShell from '../../../components/sms/SmsShell'

export default function SmsFlowsPage() {
  const { session } = useAuth()
  const toast = useToast()
  const canWrite = useMemo(() => hasPermission('automation.write', session), [session])
  const [flows, setFlows] = useState([])
  const [name, setName] = useState('')
  const [editingId, setEditingId] = useState('')
  const [editName, setEditName] = useState('')

  async function load() { setFlows(await apiGet('/api/sms/flows')) }
  useEffect(() => { load() }, [])

  async function create() {
    const optimistic = { id: `tmp-${Date.now()}`, name, status: 'Active', sentCount: 0 }
    const prev = flows
    setFlows([optimistic, ...flows])
    try { await apiPost('/api/sms/flows', { name, status: 'Active', sentCount: 0 }); setName(''); toast.success('Flow created'); load() }
    catch { setFlows(prev); toast.error('Flow create failed') }
  }

  async function update(id) {
    const prev = flows
    setFlows(flows.map((f) => f.id === id ? { ...f, name: editName } : f))
    try {
      const current = prev.find((x) => x.id === id)
      const res = await apiRequest(`/api/sms/flows/${id}`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ ...current, name: editName }) })
      if (!res.ok) throw new Error()
      setEditingId('')
      toast.success('Flow updated')
    } catch { setFlows(prev); toast.error('Flow update failed') }
  }

  async function remove(id) {
    const prev = flows
    setFlows(flows.filter((x) => x.id !== id))
    try { const res = await apiRequest(`/api/sms/flows/${id}`, { method: 'DELETE' }); if (!res.ok) throw new Error(); toast.success('Flow deleted') }
    catch { setFlows(prev); toast.error('Flow delete failed') }
  }

  return <SmsShell><div className="templates-head"><h1>SMS Active Flows</h1>{canWrite && <button className="primary" onClick={create}>Create Flow</button>}</div>{canWrite && <section className='panel dark' style={{ marginBottom: 12 }}><div className='campaign-form'><input placeholder='Flow name' value={name} onChange={(e) => setName(e.target.value)} /></div></section>}<section className="panel dark"><table className="tpl-table"><thead><tr><th>Flow</th><th>Status</th><th>Messages Sent</th><th>Action</th></tr></thead><tbody>{flows.map((f) => <tr key={f.id}><td>{editingId===f.id?<input value={editName} onChange={(e)=>setEditName(e.target.value)}/>:f.name}</td><td><span className={f.status === 'Active' ? 'badge ok' : 'badge wait'}>{f.status}</span></td><td>{f.sentCount}</td><td>{canWrite && (editingId===f.id? <><button className='primary' onClick={()=>update(f.id)}>Save</button><button className='ghost' onClick={()=>setEditingId('')}>Cancel</button></> : <><button className='ghost' onClick={()=>{setEditingId(f.id);setEditName(f.name)}}>Edit</button><button className='ghost' onClick={() => remove(f.id)}>Delete</button></>)}</td></tr>)}</tbody></table></section></SmsShell>
}
