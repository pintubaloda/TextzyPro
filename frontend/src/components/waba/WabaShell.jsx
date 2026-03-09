import { NavLink } from 'react-router-dom'
import { useAuth } from '../../auth/AuthProvider'

export default function WabaShell({ children }) {
  const { session, logout } = useAuth()

  return (
    <div className="module-stage">
      <div className="waba-shell">
        <aside className="waba-sidebar">
          <div className="waba-brand">ByeWind</div>
          <p className="sidebar-meta">{session.email || 'Not logged in'}</p>
          <p className="sidebar-meta">Role: {session.role || '-'}</p>
          <p className="sidebar-meta">Project: {session.tenantSlug || '-'}</p>
          <nav>
            <NavLink to="/modules/waba/dashboard">Dashboards</NavLink>
            <NavLink to="/modules/waba/templates">Templates</NavLink>
            <NavLink to="/modules/waba/campaigns">Campaigns</NavLink>
            <NavLink to="/modules/waba/contacts">Contacts</NavLink>
            <NavLink to="/modules/waba/live-chat">Live Chat</NavLink>
            <NavLink to="/modules/waba/chatbot">Chatbot</NavLink>
            <NavLink to="/modules/waba/frames">Design Frames</NavLink>
            <NavLink to="/modules/sms/customize">Go SMS</NavLink>
          </nav>
          <button className="ghost sidebar-logout" onClick={logout}>Logout</button>
        </aside>
        <section className="waba-main">{children}</section>
      </div>
    </div>
  )
}
