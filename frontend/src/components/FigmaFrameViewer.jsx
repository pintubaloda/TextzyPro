export default function FigmaFrameViewer({ title, subtitle, frames, activeKey, onChange }) {
  const active = frames.find((f) => f.key === activeKey) || frames[0]

  return (
    <section className="figma-viewer">
      <header className="viewer-header">
        <div>
          <h1>{title}</h1>
          {subtitle && <p>{subtitle}</p>}
        </div>
      </header>

      <nav className="frame-tabs">
        {frames.map((frame) => (
          <button
            key={frame.key}
            className={frame.key === active.key ? 'tab active' : 'tab'}
            onClick={() => onChange(frame.key)}
            type="button"
          >
            {frame.label}
          </button>
        ))}
      </nav>

      <figure className="frame-stage">
        <img src={active.src} alt={active.label} />
      </figure>
    </section>
  )
}
