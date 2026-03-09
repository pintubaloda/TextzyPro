import { Navigate, useLocation } from 'react-router-dom'
import { useAuth } from '../../auth/AuthProvider'

export default function ProtectedRoute({ children, allowRoles = [] }) {
  const { isAuthenticated, session } = useAuth()
  const location = useLocation()

  if (!isAuthenticated) return <Navigate to="/login" replace state={{ from: location.pathname }} />
  if (allowRoles.length && !allowRoles.includes((session.role || '').toLowerCase())) {
    return <Navigate to="/unauthorized" replace />
  }

  return children
}
