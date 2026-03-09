import { useEffect, useMemo, useState } from 'react'
import { apiGet, apiRequest, hasPermission } from '../../../api/client'
import { useAuth } from '../../../auth/AuthProvider'
import { useToast } from '../../../feedback/ToastProvider'
import WabaShell from '../../../components/waba/WabaShell'

export default function WabaChatbotPage() {
  const { session } = useAuth()
  const toast = useToast()
  const canWrite = useMemo(() => hasPermission('automation.write', session), [session])
  const [config, setConfig] = useState({ greeting: '', fallback: '', handoffEnabled: true })

  useEffect(() => { apiGet('/api/chatbot-config').then(setConfig).catch(() => toast.error('Load chatbot config failed')) }, [])

  async function save() {
    const prev = config
    setConfig({ ...config, updatedAtUtc: new Date().toISOString() })
    try {
      const res = await apiRequest('/api/chatbot-config', { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(config) })
      if (!res.ok) throw new Error()
      setConfig(await res.json())
      toast.success('Chatbot config saved')
    } catch { setConfig(prev); toast.error('Save chatbot config failed') }
  }

  return <WabaShell><div className="templates-head"><h1>Chatbot</h1></div><section className="panel dark"><div className="campaign-form"><label>Greeting Message</label><textarea rows={3} value={config.greeting || ''} onChange={(e) => setConfig({ ...config, greeting: e.target.value })} disabled={!canWrite} /><label>Fallback Message</label><textarea rows={3} value={config.fallback || ''} onChange={(e) => setConfig({ ...config, fallback: e.target.value })} disabled={!canWrite} /><label>Agent Handoff</label><select value={config.handoffEnabled ? 'Enabled' : 'Disabled'} onChange={(e) => setConfig({ ...config, handoffEnabled: e.target.value === 'Enabled' })} disabled={!canWrite}><option>Enabled</option><option>Disabled</option></select></div>{canWrite && <div className="actions"><button className="primary" onClick={save}>Save Bot Config</button></div>}</section></WabaShell>
}
