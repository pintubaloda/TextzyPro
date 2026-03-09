import { createContext, useContext, useMemo, useState } from 'react'
import { apiPost, authLogin, initializeMe, clearSession as clearApiSession } from '../lib/api'

const AuthContext = createContext(null)

const SESSION_KEY = 'textzy.session'

function readSession() {
  const raw = localStorage.getItem(SESSION_KEY)
  if (!raw) return { tenantSlug: '', role: '', email: '', permissions: [] }
  try {
    const parsed = JSON.parse(raw)
    return {
      tenantSlug: parsed.tenantSlug || '',
      role: parsed.role || '',
      email: parsed.email || '',
      permissions: Array.isArray(parsed.permissions) ? parsed.permissions : []
    }
  } catch {
    return { tenantSlug: '', role: '', email: '', permissions: [] }
  }
}

export function AuthProvider({ children }) {
  const [session, setSession] = useState(readSession)

  function persist(next) {
    setSession((prev) => {
      const merged = { ...prev, ...next }
      localStorage.setItem(SESSION_KEY, JSON.stringify(merged))
      return merged
    })
  }

  function clearSession() {
    const empty = { tenantSlug: '', role: '', email: '', permissions: [] }
    setSession(empty)
    clearApiSession()
  }

  async function login({ email, password }) {
    await authLogin({ email, password })
    const me = await initializeMe()
    if (!me?.email) throw new Error('Login succeeded but session/profile init failed.')
    const existing = readSession()
    persist({
      role: me.role || '',
      email: me.email || '',
      permissions: Array.isArray(me.permissions) ? me.permissions : [],
      tenantSlug: existing.tenantSlug || me.tenantSlug || ''
    })
  }

  async function logout() {
    try {
      await apiPost('/api/auth/logout', {})
    } finally {
      clearSession()
    }
  }

  const value = useMemo(() => ({
    session,
    isAuthenticated: Boolean(session.email),
    login,
    logout,
    setTenantSlug: () => {}
  }), [session])

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth() {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used within AuthProvider')
  return ctx
}
