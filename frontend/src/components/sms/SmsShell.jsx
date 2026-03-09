import { NavLink } from 'react-router-dom'
import { useAuth } from '../../auth/AuthProvider'

export default function SmsShell({ children }) {
  const { session, logout } = useAuth()

  return (
    <div className="module-stage">
      <div className="waba-shell">
        <aside className="waba-sidebar">
          <div className="waba-brand">Textzy SMS</div>
          <p className="sidebar-meta">{session.email || 'Not logged in'}</p>
          <p className="sidebar-meta">Role: {session.role || '-'}</p>
          <p className="sidebar-meta">Project: {session.tenantSlug || '-'}</p>
          <nav>
            <NavLink to="/modules/sms/customize">Customization</NavLink>
            <NavLink to="/modules/sms/flows">Active Flows</NavLink>
            <NavLink to="/modules/sms/builder">Flow Builder</NavLink>
            <NavLink to="/modules/sms/inputs">Input Fields</NavLink>
            <NavLink to="/modules/sms/frames">Design Frames</NavLink>
            <NavLink to="/modules/waba/dashboard">Go WABA</NavLink>
          </nav>
          <button className="ghost sidebar-logout" onClick={logout}>Logout</button>
        </aside>
        <section className="waba-main">{children}</section>
      </div>
    </div>
  )
}
