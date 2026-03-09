import { useState } from 'react'
import FigmaFrameViewer from '../../../components/FigmaFrameViewer'

const frames = [
  { key: 'dashboard', label: 'Dashboard (Design)', src: '/design/textzy/Dashboard.png' },
  { key: 'templates', label: 'Templates (Design)', src: '/design/textzy/Templates.png' },
  { key: 'add-template', label: 'Add Template', src: '/design/textzy/Add template.png' },
  { key: 'campaign', label: 'Campaign', src: '/design/textzy/Campaign.png' },
  { key: 'campaign-1', label: 'Campaign Step 1', src: '/design/textzy/Campaign-1.png' },
  { key: 'campaign-2', label: 'Campaign Step 2', src: '/design/textzy/Campaign-2.png' },
  { key: 'campaign-3', label: 'Campaign Step 3', src: '/design/textzy/Campaign-3.png' },
  { key: 'campaign-4', label: 'Campaign Step 4', src: '/design/textzy/Campaign-4.png' },
  { key: 'campaign-5', label: 'Campaign Step 5', src: '/design/textzy/Campaign-5.png' },
  { key: 'live-chat', label: 'Live Chat', src: '/design/textzy/Live Chat.png' },
  { key: 'contacts', label: 'Contacts', src: '/design/textzy/Contacts.png' }
]

export default function WabaFramesPage() {
  const [active, setActive] = useState('dashboard')
  return (
    <main className="page-wrap">
      <FigmaFrameViewer
        title="WABA Design Frames"
        subtitle="Use these as source for next coded screens"
        frames={frames}
        activeKey={active}
        onChange={setActive}
      />
    </main>
  )
}
