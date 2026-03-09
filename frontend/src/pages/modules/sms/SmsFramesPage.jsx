import { useState } from 'react'
import FigmaFrameViewer from '../../../components/FigmaFrameViewer'

const frames = [
  { key: 'customizes', label: 'Customization (Design)', src: '/design/textzy/customizes.png' },
  { key: 'active', label: 'Active (Design)', src: '/design/textzy/active.png' },
  { key: 'add', label: 'Add (Design)', src: '/design/textzy/add.png' },
  { key: 'add-input', label: 'Add Input (Design)', src: '/design/textzy/add inpute.png' },
  { key: 'new-input', label: 'New Input (Design)', src: '/design/textzy/new inpute.png' }
]

export default function SmsFramesPage() {
  const [active, setActive] = useState('customizes')
  return (
    <main className="page-wrap">
      <FigmaFrameViewer
        title="SMS Design Frames"
        subtitle="Reference for further pixel-level tuning"
        frames={frames}
        activeKey={active}
        onChange={setActive}
      />
    </main>
  )
}
