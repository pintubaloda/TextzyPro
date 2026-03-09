import { Link } from 'react-router-dom'

export default function UnauthorizedPage() {
  return (
    <main className="page-wrap">
      <section className="panel dark" style={{ maxWidth: 520, margin: '40px auto' }}>
        <h1>Unauthorized</h1>
        <p>Your role does not have access to this page.</p>
        <Link to="/modules/waba/dashboard">Go to Dashboard</Link>
      </section>
    </main>
  )
}
