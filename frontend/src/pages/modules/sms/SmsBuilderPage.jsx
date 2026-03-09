import { useEffect, useMemo, useState } from 'react'
import { apiGet, apiPost, hasPermission } from '../../../api/client'
import { useAuth } from '../../../auth/AuthProvider'
import { useToast } from '../../../feedback/ToastProvider'
import SmsShell from '../../../components/sms/SmsShell'

export default function SmsBuilderPage() {
  const { session } = useAuth()
  const toast = useToast()
  const canWrite = useMemo(() => hasPermission('automation.write', session), [session])
  const [flows, setFlows] = useState([])
  const [name, setName] = useState('')

  async function load() { setFlows(await apiGet('/api/sms/flows')) }
  useEffect(() => { load() }, [])

  async function addFlow() {
    const optimistic = { id: `tmp-${Date.now()}`, name, status: 'Active' }
    const prev = flows
    setFlows([optimistic, ...flows])
    try {
      await apiPost('/api/sms/flows', { name, status: 'Active', sentCount: 0 })
      setName('')
      toast.success('Flow node added')
      load()
    } catch {
      setFlows(prev)
      toast.error('Flow node add failed')
    }
  }

  return (
    <SmsShell>
      <div className="templates-head"><h1>Flow Builder</h1></div>
      {canWrite && <section className='panel dark'><div className='campaign-form'><input placeholder='New flow step name' value={name} onChange={(e) => setName(e.target.value)} /></div><div className='actions'><button className='primary' onClick={addFlow}>+ Add Flow Node</button></div></section>}
      <section className='panel dark'><div className='builder-list'>{flows.map((n) => <div className='row' key={n.id}><strong>{n.name}</strong> ({n.status})</div>)}</div></section>
    </SmsShell>
  )
}
