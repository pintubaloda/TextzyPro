import { toast } from 'sonner'

const runtimeConfig = typeof window !== 'undefined' ? (window.__APP_CONFIG__ || {}) : {}
const API_BASE =
  runtimeConfig.API_BASE ||
  process.env.REACT_APP_API_BASE ||
  process.env.VITE_API_BASE ||
  'https://textzy-backend-production.up.railway.app'
const STORAGE_KEY = 'textzy.session'
const CSRF_STORAGE_KEY = 'textzy.csrf'
const LAST_TENANT_KEY = 'textzy.lastTenantSlug'
const WABA_STATUS_CACHE_PREFIX = 'textzy.wabaStatus'
const ONE_DAY_MS = 24 * 60 * 60 * 1000
const DEFAULT_SESSION_IDLE_TIMEOUT_MINUTES = 30
const DEFAULT_SESSION_IDLE_WARNING_SECONDS = 60
const AUTH_REDIRECT_REASON_KEY = 'textzy.authRedirectReason'
let refreshPromise = null
let authRedirected = false
let stepUpUiHandler = null
const UNSAFE_METHODS = new Set(['POST', 'PUT', 'PATCH', 'DELETE'])

export function getApiBase() {
  return API_BASE
}

function readCsrfToken() {
  if (typeof document !== 'undefined') {
    const m = document.cookie.match(/(?:^|;\s*)textzy_csrf=([^;]+)/)
    if (m && m[1]) {
      try {
        return decodeURIComponent(m[1])
      } catch {
        return m[1]
      }
    }
  }

  try {
    return localStorage.getItem(CSRF_STORAGE_KEY) || ''
  } catch {
    return ''
  }
}

function persistCsrfFromResponse(res) {
  const csrf = (res.headers.get('x-csrf-token') || '').trim()
  if (!csrf) return
  try {
    localStorage.setItem(CSRF_STORAGE_KEY, csrf)
  } catch {
    // ignore storage failures
  }
}

export function getSession() {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (!raw) return { tenantSlug: '', role: '', email: '', permissions: [] }
    const p = JSON.parse(raw)
    const permissions = Array.isArray(p.permissions)
      ? p.permissions.filter((x) => typeof x === 'string' && x.trim())
      : []
    return {
      tenantSlug: p.tenantSlug || p.slug || '',
      projectName: p.projectName || '',
      role: p.role || '',
      email: p.email || '',
      permissions
    }
  } catch {
    return { tenantSlug: '', projectName: '', role: '', email: '', permissions: [] }
  }
}

export function setSession(next) {
  const current = getSession()
  const merged = { ...current, ...next }
  merged.permissions = Array.isArray(merged.permissions)
    ? merged.permissions.filter((x) => typeof x === 'string' && x.trim())
    : []
  if (merged.tenantSlug) {
    try {
      localStorage.setItem(LAST_TENANT_KEY, String(merged.tenantSlug).trim().toLowerCase())
    } catch {
      // ignore storage failures
    }
  }
  localStorage.setItem(STORAGE_KEY, JSON.stringify(merged))
  return merged
}

export function clearSession() {
  localStorage.removeItem(STORAGE_KEY)
  localStorage.removeItem(CSRF_STORAGE_KEY)
}

function getAuthRedirectMessage(reason) {
  const normalized = String(reason || '').trim().toLowerCase()
  if (normalized === 'ip_changed') return 'Your IP changed. Please login again.'
  if (normalized === 'idle_timeout') return 'Your session expired due to inactivity. Please login again.'
  if (normalized === 'ip_rejected') return 'Your current IP is not allowed. Please login again.'
  return ''
}

function persistAuthRedirectReason(reason) {
  if (!reason) return
  try {
    localStorage.setItem(AUTH_REDIRECT_REASON_KEY, String(reason).trim().toLowerCase())
  } catch {
    // ignore storage failures
  }
}

export function consumeAuthRedirectReason() {
  try {
    const reason = (localStorage.getItem(AUTH_REDIRECT_REASON_KEY) || '').trim().toLowerCase()
    if (reason) localStorage.removeItem(AUTH_REDIRECT_REASON_KEY)
    return reason
  } catch {
    return ''
  }
}

function redirectToLogin(reason = '') {
  clearSession()
  if (!authRedirected && typeof window !== 'undefined' && window.location.pathname !== '/login') {
    authRedirected = true
    const normalizedReason = String(reason || '').trim().toLowerCase()
    if (normalizedReason) persistAuthRedirectReason(normalizedReason)
    const nextUrl = normalizedReason ? `/login?reason=${encodeURIComponent(normalizedReason)}` : '/login'
    window.location.assign(nextUrl)
  }
}

export function getSessionIdleTimeoutMs() {
  const raw =
    runtimeConfig.SESSION_IDLE_TIMEOUT_MINUTES ||
    process.env.REACT_APP_SESSION_IDLE_TIMEOUT_MINUTES ||
    process.env.VITE_SESSION_IDLE_TIMEOUT_MINUTES ||
    String(DEFAULT_SESSION_IDLE_TIMEOUT_MINUTES)
  const parsed = Number.parseInt(String(raw || '').trim(), 10)
  const minutes = Number.isFinite(parsed) && parsed > 0 ? parsed : DEFAULT_SESSION_IDLE_TIMEOUT_MINUTES
  return minutes * 60 * 1000
}

export function getSessionIdleWarningMs() {
  const raw =
    runtimeConfig.SESSION_IDLE_WARNING_SECONDS ||
    process.env.REACT_APP_SESSION_IDLE_WARNING_SECONDS ||
    process.env.VITE_SESSION_IDLE_WARNING_SECONDS ||
    String(DEFAULT_SESSION_IDLE_WARNING_SECONDS)
  const parsed = Number.parseInt(String(raw || '').trim(), 10)
  const seconds = Number.isFinite(parsed) && parsed > 0 ? parsed : DEFAULT_SESSION_IDLE_WARNING_SECONDS
  return seconds * 1000
}

export async function authLogout() {
  try {
    await baseFetch('/api/auth/logout', { method: 'POST' }, true)
  } catch {
    // Local cleanup still needs to happen when the server session is already gone.
  } finally {
    clearSession()
  }
}

export function registerStepUpUiHandler(handler) {
  stepUpUiHandler = handler
  return () => {
    if (stepUpUiHandler === handler) stepUpUiHandler = null
  }
}

export function getLastTenantSlug() {
  try {
    return (localStorage.getItem(LAST_TENANT_KEY) || '').trim().toLowerCase()
  } catch {
    return ''
  }
}

const ROLE_PERMISSION_FALLBACK = {
  owner: [
    'contacts.read', 'contacts.write', 'campaigns.read', 'campaigns.write', 'templates.read', 'templates.write',
    'automation.read', 'automation.write', 'inbox.read', 'inbox.write', 'billing.read', 'billing.write',
    'api.read', 'api.write'
  ],
  admin: [
    'contacts.read', 'contacts.write', 'campaigns.read', 'campaigns.write', 'templates.read', 'templates.write',
    'automation.read', 'automation.write', 'inbox.read', 'inbox.write', 'billing.read', 'billing.write', 'api.read', 'api.write'
  ],
  manager: [
    'contacts.read', 'contacts.write', 'campaigns.read', 'campaigns.write', 'templates.read',
    'automation.read', 'automation.write', 'inbox.read', 'inbox.write', 'billing.read', 'api.read'
  ],
  support: ['inbox.read', 'inbox.write', 'contacts.read', 'templates.read', 'api.read'],
  marketing: ['campaigns.read', 'campaigns.write', 'templates.read', 'templates.write', 'contacts.read', 'api.read'],
  finance: ['billing.read', 'billing.write', 'campaigns.read', 'api.read'],
  super_admin: [
    'contacts.read', 'contacts.write', 'campaigns.read', 'campaigns.write', 'templates.read', 'templates.write',
    'automation.read', 'automation.write', 'inbox.read', 'inbox.write', 'billing.read', 'billing.write',
    'api.read', 'api.write', 'platform.tenants.manage', 'platform.settings.read', 'platform.settings.write'
  ]
}

export function hasPermission(permission, session = getSession()) {
  const target = String(permission || '').trim().toLowerCase()
  if (!target) return false
  const explicit = Array.isArray(session?.permissions) ? session.permissions : []
  if (explicit.length > 0) {
    return explicit.some((x) => String(x || '').toLowerCase() === target)
  }
  const role = String(session?.role || '').toLowerCase()
  return (ROLE_PERMISSION_FALLBACK[role] || []).includes(target)
}

function getWabaStatusCacheKey() {
  const s = getSession()
  const slug = (s.tenantSlug || '').trim().toLowerCase()
  return `${WABA_STATUS_CACHE_PREFIX}:${slug || 'default'}`
}

function readWabaStatusCache() {
  try {
    const raw = localStorage.getItem(getWabaStatusCacheKey())
    if (!raw) return null
    const parsed = JSON.parse(raw)
    if (!parsed || typeof parsed !== 'object' || !parsed.data) return null
    return parsed
  } catch {
    return null
  }
}

function writeWabaStatusCache(data) {
  try {
    const payload = {
      fetchedAt: Date.now(),
      data
    }
    localStorage.setItem(getWabaStatusCacheKey(), JSON.stringify(payload))
  } catch {
    // ignore storage failures
  }
}

export function invalidateWabaStatusCache() {
  try {
    localStorage.removeItem(getWabaStatusCacheKey())
  } catch {
    // ignore storage failures
  }
}

async function baseFetch(path, options = {}, useAuth = true) {
  const s = getSession()
  const headers = {
    ...(options.headers || {})
  }
  const method = (options.method || 'GET').toUpperCase()
  const tenantOptionalPrefixes = [
    '/api/auth/',
    '/api/public/',
    '/api/platform/',
    '/api/payment/webhooks',
    '/api/payments/webhook/'
  ]
  const requiresTenant = useAuth && !tenantOptionalPrefixes.some((p) => path.startsWith(p))
  if (requiresTenant && s.tenantSlug) headers['X-Tenant-Slug'] = s.tenantSlug
  if (requiresTenant && !headers['X-Tenant-Slug']) {
    if (typeof window !== 'undefined' && window.location.pathname !== '/projects') {
      window.location.assign('/projects')
    }
    throw new Error('Missing tenant context. Please select project.')
  }

  if (options.body && !(options.body instanceof FormData) && !headers['Content-Type']) {
    headers['Content-Type'] = 'application/json'
  }
  if (UNSAFE_METHODS.has(method) && !headers['X-CSRF-Token']) {
    const csrfToken = readCsrfToken()
    if (csrfToken) headers['X-CSRF-Token'] = csrfToken
  }

  const res = await fetch(`${API_BASE}${path}`, { ...options, headers, credentials: 'include', cache: 'no-store' })
  persistCsrfFromResponse(res)
  return res
}

async function readErrorMessage(res, fallback) {
  const text = await res.text().catch(() => '')
  return text || `${fallback} (${res.status})`
}

async function buildApiError(res, fallback) {
  const text = await res.text().catch(() => '')
  if (text) {
    try {
      const parsed = JSON.parse(text)
      if (parsed && typeof parsed === 'object') {
        const err = new Error(parsed.message || parsed.title || fallback)
        Object.assign(err, parsed)
        err.status = res.status
        err.rawBody = text
        return err
      }
    } catch {
      // ignore non-json
    }
  }
  const err = new Error(text || `${fallback} (${res.status})`)
  err.status = res.status
  err.rawBody = text
  return err
}

async function performStepUp(detail = {}) {
  if (typeof stepUpUiHandler !== 'function') {
    throw new Error(detail?.message || 'Additional verification is required.')
  }

  const requestRes = await baseFetch('/api/auth/step-up/request', {
    method: 'POST',
    body: JSON.stringify({
      action: detail?.action || 'sensitive_action',
      title: detail?.title || '',
      message: detail?.message || ''
    })
  }, true)

  if (!requestRes.ok) {
    throw await buildApiError(requestRes, 'Failed to start step-up verification')
  }

  const challenge = await requestRes.json().catch(() => ({}))
  let lastError = ''
  for (let attempt = 0; attempt < 3; attempt += 1) {
    const code = await stepUpUiHandler({
      ...challenge,
      title: challenge?.title || detail?.title || 'Additional verification required',
      message: challenge?.message || detail?.message || 'Enter the code from your authenticator app.',
      errorMessage: lastError
    })

    if (!code) {
      throw new Error('Verification cancelled.')
    }

    const verifyRes = await baseFetch('/api/auth/step-up/verify', {
      method: 'POST',
      body: JSON.stringify({
        challengeToken: challenge?.challengeToken || '',
        code
      })
    }, true)

    if (verifyRes.ok) {
      return verifyRes.json().catch(() => ({ ok: true }))
    }

    const verifyError = await buildApiError(verifyRes, 'Step-up verification failed')
    const verifyMessage = String(verifyError?.message || '')
    if (verifyRes.status === 400 && /authenticator code|verification/i.test(verifyMessage)) {
      lastError = verifyMessage
      continue
    }
    throw verifyError
  }

  throw new Error(lastError || 'Step-up verification failed.')
}

export async function ensureStepUp(detail = {}) {
  await performStepUp(detail)
  return true
}

async function refresh() {
  if (refreshPromise) return refreshPromise
  refreshPromise = (async () => {
    const res = await baseFetch('/api/auth/refresh', { method: 'POST' }, true)
    if (!res.ok) {
      const reason = (res.headers.get('x-auth-reason') || '').trim().toLowerCase()
      if (res.status === 401) redirectToLogin(reason)
      return false
    }
    await res.json().catch(() => ({}))
    return true
  })()
  try {
    return await refreshPromise
  } finally {
    refreshPromise = null
  }
}

export async function refreshSession() {
  return refresh()
}

export async function apiRequest(path, options = {}) {
  let res = await baseFetch(path, options, true)
  if (res.status !== 401) return res
  const firstFailureReason = (res.headers.get('x-auth-reason') || '').trim().toLowerCase()
  const ok = await refresh()
  if (!ok) {
    redirectToLogin(firstFailureReason)
    return res
  }
  res = await baseFetch(path, options, true)
  if (res.status === 401) {
    const secondFailureReason = (res.headers.get('x-auth-reason') || '').trim().toLowerCase()
    redirectToLogin(secondFailureReason || firstFailureReason)
  }
  return res
}

export async function apiGet(path) {
  const res = await apiRequest(path)
  if (!res.ok) throw new Error(`GET ${path} failed (${res.status})`)
  const text = await res.text()
  if (!text || !text.trim()) return null
  try {
    return JSON.parse(text)
  } catch {
    throw new Error(`GET ${path} returned invalid JSON`)
  }
}

export async function apiGetBlob(path) {
  const res = await apiRequest(path)
  if (!res.ok) {
    const msg = await res.text().catch(() => "")
    throw new Error(msg || `GET ${path} failed (${res.status})`)
  }
  return res.blob()
}

export async function apiPost(path, body) {
  const headers = {}
  if (path === '/api/messages/send') {
    headers['Idempotency-Key'] = body?.idempotencyKey || buildIdempotencyKey('msg')
  }
  const res = await apiRequest(path, { method: 'POST', headers, body: JSON.stringify(body) })
  if (res.status === 428) {
    const err = await buildApiError(res, `POST ${path} failed`)
    if (err?.stepUpRequired) {
      await performStepUp(err)
      return apiPost(path, body)
    }
    throw err
  }
  if (!res.ok) {
    throw await buildApiError(res, `POST ${path} failed`)
  }
  const text = await res.text()
  return text ? JSON.parse(text) : null
}

export async function apiPostForm(path, formData, headers = {}) {
  const reqHeaders = { ...headers }
  if (path === '/api/messages/upload-whatsapp-media') {
    reqHeaders['Idempotency-Key'] = reqHeaders['Idempotency-Key'] || buildIdempotencyKey('media')
  }
  const res = await apiRequest(path, { method: 'POST', headers: reqHeaders, body: formData })
  if (!res.ok) {
    const msg = await res.text()
    throw new Error(msg || `POST ${path} failed (${res.status})`)
  }
  const text = await res.text()
  return text ? JSON.parse(text) : null
}

export async function apiPut(path, body) {
  const res = await apiRequest(path, { method: 'PUT', body: JSON.stringify(body) })
  if (res.status === 428) {
    const err = await buildApiError(res, `PUT ${path} failed`)
    if (err?.stepUpRequired) {
      await performStepUp(err)
      return apiPut(path, body)
    }
    throw err
  }
  if (!res.ok) throw await buildApiError(res, `PUT ${path} failed`)
  const text = await res.text()
  return text ? JSON.parse(text) : null
}

export async function apiPatch(path, body) {
  const res = await apiRequest(path, { method: 'PATCH', body: JSON.stringify(body) })
  if (res.status === 428) {
    const err = await buildApiError(res, `PATCH ${path} failed`)
    if (err?.stepUpRequired) {
      await performStepUp(err)
      return apiPatch(path, body)
    }
    throw err
  }
  if (!res.ok) {
    throw await buildApiError(res, `PATCH ${path} failed`)
  }
  const text = await res.text()
  return text ? JSON.parse(text) : null
}

export async function apiDelete(path) {
  const res = await apiRequest(path, { method: 'DELETE' })
  if (!res.ok && res.status !== 204) throw new Error(`DELETE ${path} failed (${res.status})`)
  return true
}

export function buildIdempotencyKey(prefix = "msg") {
  const rand = Math.random().toString(36).slice(2, 10);
  return `${prefix}-${Date.now()}-${rand}`;
}

export async function authLogin({ email, password, tenantSlug, emailVerificationId }) {
  const headers = { 'Content-Type': 'application/json' }
  if (tenantSlug) headers['X-Tenant-Slug'] = tenantSlug
  const res = await fetch(`${API_BASE}/api/auth/login`, {
    method: 'POST',
    headers,
    credentials: 'include',
    body: JSON.stringify({ email, password, tenantSlug: tenantSlug || '', emailVerificationId: emailVerificationId || '' })
  })
  persistCsrfFromResponse(res)
  if (!res.ok) {
    const msg = await readErrorMessage(res, 'Login failed')
    throw new Error(msg)
  }

  const raw = await res.text()
  if (!raw || !raw.trim()) {
    return {}
  }

  let data = null
  try {
    data = JSON.parse(raw)
  } catch {
    throw new Error('Login response is not valid JSON. Check backend proxy/deployment.')
  }

  if (data?.requiresTwoFactor) {
    return data
  }
  return data
}

export async function verifyLoginTwoFactor({ challengeToken, code, tenantSlug }) {
  const headers = { 'Content-Type': 'application/json' }
  if (tenantSlug) headers['X-Tenant-Slug'] = tenantSlug
  const res = await fetch(`${API_BASE}/api/auth/two-factor/verify-login`, {
    method: 'POST',
    headers,
    credentials: 'include',
    body: JSON.stringify({ challengeToken, code })
  })
  persistCsrfFromResponse(res)
  if (!res.ok) {
    throw await buildApiError(res, 'Two-factor verification failed')
  }
  const raw = await res.text()
  const data = raw ? JSON.parse(raw) : {}
  return data
}

export async function authRequestEmailOtp({ email, purpose = 'login' }) {
  const res = await fetch(`${API_BASE}/api/auth/email-verification/request`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
    body: JSON.stringify({ email, purpose })
  })
  if (!res.ok) {
    const msg = await readErrorMessage(res, 'Failed to send OTP')
    throw new Error(msg)
  }
  return res.json()
}

export async function authEmailOtpStatus({ email, verificationId, purpose = 'login' }) {
  const q = new URLSearchParams()
  q.set('verificationId', verificationId || '')
  q.set('purpose', purpose)
  if (email) q.set('email', email)
  const res = await fetch(`${API_BASE}/api/auth/email-verification/status?${q.toString()}`, {
    method: 'GET',
    credentials: 'include'
  })
  if (!res.ok) {
    const msg = await readErrorMessage(res, 'Failed to read OTP status')
    throw new Error(msg)
  }
  return res.json()
}

export async function authVerifyEmailOtp({ email, verificationId, otp, purpose = 'login' }) {
  const res = await fetch(`${API_BASE}/api/auth/email-verification/verify`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
    body: JSON.stringify({ email, verificationId, otp, purpose })
  })
  if (!res.ok) {
    const msg = await readErrorMessage(res, 'OTP verification failed')
    throw new Error(msg)
  }
  return res.json()
}

export async function checkApiHealth() {
  const res = await fetch(`${API_BASE}/api/public/plans`, {
    method: 'GET',
    credentials: 'include',
    cache: 'no-store'
  })
  if (!res.ok) {
    throw new Error(`Backend unreachable at ${API_BASE} (status ${res.status}).`)
  }
  return true
}

export async function getPublicMobileDownloadInfo() {
  const res = await fetch(`${API_BASE}/api/public/mobile/download`, {
    method: 'GET',
    credentials: 'include',
    cache: 'no-store'
  })
  if (!res.ok) {
    const msg = await readErrorMessage(res, 'Failed to load mobile download info')
    throw new Error(msg)
  }
  return res.json()
}

export async function getPublicAppUpdateManifest({ platform = '', appVersion = '' } = {}) {
  const query = new URLSearchParams()
  if (platform) query.set('platform', platform)
  if (appVersion) query.set('appVersion', appVersion)
  const qs = query.toString()
  const url = `${API_BASE}/api/public/app-updates/manifest${qs ? `?${qs}` : ''}`
  const res = await fetch(url, {
    method: 'GET',
    credentials: 'include',
    cache: 'no-store'
  })
  if (!res.ok) {
    const msg = await readErrorMessage(res, 'Failed to load app update manifest')
    throw new Error(msg)
  }
  return res.json()
}

export async function authAcceptInvite({ token, fullName, password }) {
  const res = await fetch(`${API_BASE}/api/auth/accept-invite`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
    body: JSON.stringify({ token, fullName, password })
  })
  persistCsrfFromResponse(res)
  if (!res.ok) {
    const msg = await res.text()
    throw new Error(msg || 'Failed to accept invite')
  }
  const data = await res.json()
  return data
}

export async function authInvitePreview({ token }) {
  const q = new URLSearchParams()
  q.set("token", token || "")
  const res = await fetch(`${API_BASE}/api/auth/invite-preview?${q.toString()}`, {
    method: "GET",
    credentials: "include"
  })
  if (!res.ok) {
    const msg = await res.text().catch(() => "")
    throw new Error(msg || "Failed to load invite details")
  }
  return res.json()
}

export async function initializeMe() {
  try {
    const me = await apiGet('/api/auth/me')
    setSession({
      role: me.role || '',
      email: me.email || '',
      tenantSlug: me.tenantSlug || getSession().tenantSlug || '',
      permissions: Array.isArray(me.permissions) ? me.permissions : []
    })
    return me
  } catch {
    return null
  }
}

export async function authProjects() {
  return apiGet('/api/auth/projects')
}

export async function getAppBootstrap() {
  return apiGet('/api/auth/app-bootstrap')
}

export async function getConnectedDevices() {
  return apiGet('/api/auth/devices')
}

export async function createPairingQr(payload = {}) {
  return apiPost('/api/auth/devices/pair-qr', payload)
}

export async function getPairingQrImage() {
  return apiGet('/api/auth/devices/pair-qr-image')
}

export async function removeConnectedDevice(deviceId) {
  return apiDelete(`/api/auth/devices/${encodeURIComponent(deviceId)}`)
}

export async function createProject(name) {
  const data = await apiPost('/api/auth/projects', { name })
  setSession({
    tenantSlug: data?.slug || '',
    projectName: data?.name || '',
    role: data?.role || 'owner',
    permissions: Array.isArray(data?.permissions) ? data.permissions : []
  })
  invalidateWabaStatusCache()
  return data
}

export async function switchProject(slug) {
  const data = await apiPost('/api/auth/switch-project', { slug })
  setSession({
    tenantSlug: data?.tenantSlug || slug,
    projectName: data?.projectName || '',
    role: data?.role || '',
    permissions: Array.isArray(data?.permissions) ? data.permissions : []
  })
  invalidateWabaStatusCache()
  return data
}

export function requireAuthOrRedirect(navigate) {
  const s = getSession()
  if (!s.email) {
    navigate('/login')
    return false
  }
  return true
}

export function notifyApiError(message) {
  toast.error(message)
}

export async function wabaStartOnboarding() {
  const out = await apiPost('/api/waba/onboarding/start', {})
  invalidateWabaStatusCache()
  return out
}

export async function wabaGetOnboardingStatus(options = {}) {
  const force = !!options.force
  const cache = readWabaStatusCache()
  if (!force && cache?.data) {
    const age = Date.now() - Number(cache.fetchedAt || 0)
    const isConnected = !!cache.data?.isConnected || !!cache.data?.readyToSend || String(cache.data?.state || '').toLowerCase() === 'ready'
    if (isConnected && age < ONE_DAY_MS) {
      return cache.data
    }
    if (!isConnected) {
      // Pending/disconnected should still auto-refresh periodically so a newly
      // connected number is reflected without requiring manual refresh.
      const PENDING_CACHE_MS = 60 * 1000
      if (age < PENDING_CACHE_MS) return cache.data
    }
  }
  const data = await apiGet('/api/waba/onboarding/status')
  writeWabaStatusCache(data)
  return data
}

export async function wabaExchangeCode(code) {
  const out = await apiPost('/api/waba/embedded-signup/exchange', { code })
  invalidateWabaStatusCache()
  return out
}

export async function wabaGetEmbeddedConfig() {
  return apiGet('/api/waba/embedded-config')
}

export async function wabaRecheckOnboarding() {
  const out = await apiPost('/api/waba/onboarding/recheck', {})
  invalidateWabaStatusCache()
  return out
}

export async function wabaDisconnectOnboarding() {
  const out = await apiPost('/api/waba/onboarding/disconnect', {})
  invalidateWabaStatusCache()
  return out
}

export async function wabaReuseExisting(code) {
  return apiPost('/api/waba/onboarding/reuse-existing', { code })
}

export async function wabaMapExisting(payload) {
  const out = await apiPost('/api/waba/onboarding/map-existing', payload || {})
  invalidateWabaStatusCache()
  return out
}

export async function getPlatformSettings(scope) {
  return apiGet(`/api/platform/settings/${scope}`)
}

export async function savePlatformSettings(scope, values) {
  return apiPut(`/api/platform/settings/${scope}`, values)
}

export async function getSupportContext() {
  return apiGet('/api/support/context')
}

export async function getSupportTickets(filters = {}) {
  const q = new URLSearchParams()
  if (filters.status) q.set('status', filters.status)
  if (filters.service) q.set('service', filters.service)
  if (filters.q) q.set('q', filters.q)
  if (filters.page) q.set('page', String(filters.page))
  if (filters.pageSize) q.set('pageSize', String(filters.pageSize))
  return apiGet(`/api/support/tickets${q.toString() ? `?${q.toString()}` : ''}`)
}

export async function getSupportTicketDetails(ticketId) {
  return apiGet(`/api/support/tickets/${encodeURIComponent(ticketId)}`)
}

export async function createSupportTicket(payload) {
  return apiPost('/api/support/tickets', payload || {})
}

export async function replySupportTicket(ticketId, payload) {
  return apiPost(`/api/support/tickets/${encodeURIComponent(ticketId)}/reply`, payload || {})
}

export async function reopenSupportTicket(ticketId, payload = {}) {
  return apiPost(`/api/support/tickets/${encodeURIComponent(ticketId)}/reopen`, payload || {})
}

export async function testPlatformSmtp(email) {
  return apiPost('/api/platform/settings/smtp/test', { email })
}

export async function diagnosePlatformSmtp(payload = {}) {
  return apiPost('/api/platform/settings/smtp/diagnose', payload || {})
}

export async function testPlatformSms(payload = {}) {
  return apiPost('/api/platform/settings/sms/test', payload || {})
}

export async function testPlatformPaymentGateway(payload = {}) {
  return apiPost('/api/platform/settings/payment-gateway/test', payload || {})
}

export async function listSmsTemplates(status = '') {
  const q = status ? `?status=${encodeURIComponent(status)}` : ''
  return apiGet(`/api/sms/templates${q}`)
}

export async function createSmsTemplate(payload) {
  return apiPost('/api/sms/templates', payload)
}

export async function updateSmsTemplate(id, payload) {
  return apiPut(`/api/sms/templates/${id}`, payload)
}

export async function deleteSmsTemplate(id) {
  return apiDelete(`/api/sms/templates/${id}`)
}

export async function setSmsTemplateStatus(id, payload) {
  return apiPost(`/api/sms/templates/${id}/status`, payload)
}

export async function sendSmsTestFromDashboard(payload) {
  const recipient = String(payload?.phone || '').trim()
  const useTemplate = !!payload?.useTemplate
  const body = String(payload?.message || '').trim()
  if (!recipient) throw new Error('Phone is required')
  if (!useTemplate && !body) throw new Error('Message is required')

  const req = {
    recipient,
    channel: 'Sms',
    body: useTemplate ? '' : body,
    useTemplate
  }

  if (useTemplate) {
    req.templateName = String(payload?.templateName || '').trim()
    req.templateLanguageCode = String(payload?.templateLanguageCode || 'en').trim() || 'en'
    req.templateParameters = Array.isArray(payload?.templateParameters) ? payload.templateParameters : []
    if (!req.templateName) throw new Error('Template name is required')
  }

  return apiPost('/api/messages/send', req)
}

export async function importApprovedSmsTemplatesCsv(file) {
  if (!file) throw new Error('CSV file is required')
  const fd = new FormData()
  fd.append('file', file)
  const res = await apiRequest('/api/sms/templates/import-approved-csv', {
    method: 'POST',
    body: fd
  })
  if (!res.ok) {
    const msg = await res.text().catch(() => '')
    throw new Error(msg || `CSV import failed (${res.status})`)
  }
  return res.json()
}

export async function getSmsComplianceKpis() {
  return apiGet('/api/sms/compliance/kpis')
}

export async function listSmsOptOuts(take = 300) {
  return apiGet(`/api/sms/compliance/opt-outs?take=${encodeURIComponent(String(take))}`)
}

export async function addSmsOptOut(payload) {
  return apiPost('/api/sms/compliance/opt-outs', payload)
}

export async function removeSmsOptOut(id) {
  return apiDelete(`/api/sms/compliance/opt-outs/${id}`)
}

export async function listSmsComplianceEvents(take = 200) {
  return apiGet(`/api/sms/compliance/events?take=${encodeURIComponent(String(take))}`)
}

export async function listSmsBillingLedger(take = 200) {
  return apiGet(`/api/sms/compliance/billing-ledger?take=${encodeURIComponent(String(take))}`)
}

export async function getTenantSmsGatewayReportStatus() {
  return apiGet('/api/sms/gateway-report/status')
}

export async function listTenantSmsGatewayReport({ isSuccess = "", limit = 200 } = {}) {
  const q = new URLSearchParams()
  if (isSuccess !== "") q.set("isSuccess", String(isSuccess))
  q.set("limit", String(limit))
  return apiGet(`/api/sms/gateway-report?${q.toString()}`)
}

export async function getPlatformEmailReport({ days = 7, take = 100 } = {}) {
  const q = new URLSearchParams()
  q.set('days', String(days))
  q.set('take', String(take))
  return apiGet(`/api/platform/email-report?${q.toString()}`)
}

export async function exportPlatformSqlBackup() {
  const res = await apiRequest('/api/platform/backup/sql')
  if (!res.ok) {
    const msg = await res.text().catch(() => '')
    throw new Error(msg || `GET /api/platform/backup/sql failed (${res.status})`)
  }
  return res.blob()
}

export async function listSmsSenders() {
  try {
    return await apiGet('/api/sms/senders')
  } catch (e) {
    return apiGet('/api/sms/sender')
  }
}

export async function createSmsSender(payload) {
  try {
    return await apiPost('/api/sms/senders', payload)
  } catch (e) {
    return apiPost('/api/sms/sender', payload)
  }
}

export async function updateSmsSender(id, payload) {
  try {
    return await apiPut(`/api/sms/senders/${id}`, payload)
  } catch {
    return apiPut(`/api/sms/sender/${id}`, payload)
  }
}

export async function archiveSmsSender(id) {
  try {
    return await apiDelete(`/api/sms/senders/${id}`)
  } catch {
    return apiDelete(`/api/sms/sender/${id}`)
  }
}

export async function getSmsSenderStats() {
  try {
    return await apiGet('/api/sms/senders/stats')
  } catch {
    return apiGet('/api/sms/sender/stats')
  }
}

export async function getPlatformWebhookLogs({ provider = "", limit = 100 } = {}) {
  const q = new URLSearchParams()
  if (provider) q.set("provider", provider)
  q.set("limit", String(limit))
  return apiGet(`/api/platform/webhook-logs?${q.toString()}`)
}

export async function getPlatformRequestLogs({ tenantId = "", method = "", statusCode = "", pathContains = "", limit = 200 } = {}) {
  const q = new URLSearchParams()
  if (tenantId) q.set("tenantId", tenantId)
  if (method) q.set("method", method)
  if (statusCode) q.set("statusCode", String(statusCode))
  if (pathContains) q.set("pathContains", pathContains)
  q.set("limit", String(limit))
  return apiGet(`/api/platform/request-logs?${q.toString()}`)
}

export async function getPlatformSmsGatewayLogs({
  provider = "",
  tenantId = "",
  isSuccess = "",
  recipientContains = "",
  fromUtc = "",
  toUtc = "",
  limit = 200
} = {}) {
  const q = new URLSearchParams()
  if (provider) q.set("provider", provider)
  if (tenantId) q.set("tenantId", tenantId)
  if (isSuccess !== "") q.set("isSuccess", String(isSuccess))
  if (recipientContains) q.set("recipientContains", recipientContains)
  if (fromUtc) q.set("fromUtc", fromUtc)
  if (toUtc) q.set("toUtc", toUtc)
  q.set("limit", String(limit))
  return apiGet(`/api/platform/sms-gateway-logs?${q.toString()}`)
}

export async function getPlatformQueueHealth() {
  return apiGet('/api/platform/queue-health')
}

export async function getPlatformMobileTelemetry({ take = 200, days = 1 } = {}) {
  const q = new URLSearchParams()
  q.set("take", String(take))
  q.set("days", String(days))
  return apiGet(`/api/platform/mobile-telemetry?${q.toString()}`)
}

export async function getPlatformSecuritySignals({ status = "open", limit = 100 } = {}) {
  const q = new URLSearchParams()
  if (status) q.set("status", status)
  q.set("limit", String(limit))
  return apiGet(`/api/platform/security/signals?${q.toString()}`)
}

export async function resolvePlatformSecuritySignal(id) {
  return apiPost(`/api/platform/security/signals/${id}/resolve`, {})
}

export async function getPlatformSecurityControls(tenantId) {
  const q = new URLSearchParams()
  q.set("tenantId", tenantId)
  return apiGet(`/api/platform/security/controls?${q.toString()}`)
}

export async function getPlatformSecurityReport({
  tenantId = "",
  userId = "",
  actionContains = "",
  sessionStatus = "all",
  fromUtc = "",
  toUtc = "",
  limit = 200
} = {}) {
  const q = new URLSearchParams()
  if (tenantId) q.set("tenantId", tenantId)
  if (userId) q.set("userId", userId)
  if (actionContains) q.set("actionContains", actionContains)
  if (sessionStatus) q.set("sessionStatus", sessionStatus)
  if (fromUtc) q.set("fromUtc", fromUtc)
  if (toUtc) q.set("toUtc", toUtc)
  q.set("limit", String(limit))
  return apiGet(`/api/platform/security/report?${q.toString()}`)
}

export async function exportPlatformSecurityReport(filters = {}) {
  const q = new URLSearchParams()
  if (filters.tenantId) q.set("tenantId", filters.tenantId)
  if (filters.userId) q.set("userId", filters.userId)
  if (filters.actionContains) q.set("actionContains", filters.actionContains)
  if (filters.sessionStatus) q.set("sessionStatus", filters.sessionStatus)
  if (filters.fromUtc) q.set("fromUtc", filters.fromUtc)
  if (filters.toUtc) q.set("toUtc", filters.toUtc)
  q.set("limit", String(filters.limit || 1000))
  return apiGetBlob(`/api/platform/security/report/export?${q.toString()}`)
}

export async function revokePlatformSession(sessionId) {
  return apiPost(`/api/platform/security/sessions/${sessionId}/revoke`, {})
}

export async function blockPlatformSessionIp(sessionId) {
  return apiPost(`/api/platform/security/sessions/${sessionId}/block-ip`, {})
}

export async function getPlatformSecurityIpRules() {
  return apiGet("/api/platform/security/ip-rules")
}

export async function createPlatformSecurityIpRule(payload) {
  return apiPost("/api/platform/security/ip-rules", payload)
}

export async function deletePlatformSecurityIpRule(ruleId) {
  return apiDelete(`/api/platform/security/ip-rules/${ruleId}`)
}

export async function upsertPlatformSecurityControls(payload) {
  return apiPut("/api/platform/security/controls", payload)
}

export async function purgePlatformQueue(queue) {
  return apiPost("/api/platform/security/queue/purge", { queue })
}

export async function listWabaErrorPolicies() {
  return apiGet('/api/platform/waba-error-policies')
}

export async function upsertWabaErrorPolicy(payload) {
  return apiPost('/api/platform/waba-error-policies', payload)
}

export async function deactivateWabaErrorPolicy(code) {
  return apiDelete(`/api/platform/waba-error-policies/${encodeURIComponent(code)}`)
}

export async function getPlatformWebhookAnalytics(tenantId = '', days = 7) {
  const q = new URLSearchParams()
  if (tenantId) q.set('tenantId', tenantId)
  q.set('days', String(days))
  return apiGet(`/api/platform/webhook-analytics?${q.toString()}`)
}

export async function getPlatformWabaOnboardingSummary() {
  return apiGet('/api/platform/waba/onboarding-summary')
}

export async function cancelPlatformWabaRequest(tenantId, reason = '') {
  return apiPost('/api/platform/waba/cancel-request', { tenantId, reason })
}

export async function getPlatformWabaLifecycle(tenantId) {
  const q = new URLSearchParams()
  q.set('tenantId', tenantId)
  return apiGet(`/api/platform/waba/lifecycle?${q.toString()}`)
}

export async function reissuePlatformWabaToken(tenantId) {
  return apiPost('/api/platform/waba/lifecycle/reissue-token', { tenantId })
}

export async function deactivatePlatformWabaLifecycle(tenantId) {
  return apiPost('/api/platform/waba/lifecycle/deactivate', { tenantId })
}

export async function getWabaDebugTenantProbe() {
  return apiGet('/api/waba/debug/tenant-probe')
}

export async function getWabaDebugWebhookHealth() {
  return apiGet('/api/waba/debug/webhook-health')
}

export async function platformLookupByPhone(tenantId, phoneNumberId) {
  const q = new URLSearchParams()
  if (tenantId) q.set('tenantId', tenantId)
  q.set('phoneNumberId', phoneNumberId)
  return apiGet(`/api/platform/waba/lookup/by-phone?${q.toString()}`)
}

export async function platformLookupByWaba(tenantId, wabaId) {
  const q = new URLSearchParams()
  if (tenantId) q.set('tenantId', tenantId)
  q.set('wabaId', wabaId)
  return apiGet(`/api/platform/waba/lookup/by-waba?${q.toString()}`)
}

export async function getTenantWebhookAnalytics(days = 7) {
  const q = new URLSearchParams()
  q.set('days', String(days))
  return apiGet(`/api/analytics/webhook?${q.toString()}`)
}

export async function getTenantAnalyticsOverview(days = 30) {
  const q = new URLSearchParams()
  q.set('days', String(days))
  return apiGet(`/api/analytics/overview?${q.toString()}`)
}

export async function getTenantIdempotencyDiagnostics({ status = '', staleMinutes = 30, limit = 200 } = {}) {
  const q = new URLSearchParams()
  if (status) q.set('status', status)
  q.set('staleMinutes', String(staleMinutes))
  q.set('limit', String(limit))
  return apiGet(`/api/idempotency/diagnostics?${q.toString()}`)
}

export async function getPlatformIdempotencyDiagnostics({ tenantId, status = '', staleMinutes = 30, limit = 300 } = {}) {
  const q = new URLSearchParams()
  if (tenantId) q.set('tenantId', tenantId)
  if (status) q.set('status', status)
  q.set('staleMinutes', String(staleMinutes))
  q.set('limit', String(limit))
  return apiGet(`/api/platform/idempotency-diagnostics?${q.toString()}`)
}

export async function listPaymentWebhooks() {
  return apiGet('/api/platform/payment-webhooks')
}

export async function autoCreatePaymentWebhook(provider) {
  return apiPost('/api/platform/payment-webhooks/auto-create', { provider })
}

export async function upsertPaymentWebhook(payload) {
  return apiPut('/api/platform/payment-webhooks', payload)
}

export async function listPlatformBillingPlans() {
  return apiGet('/api/platform/billing/plans')
}

export async function createPlatformBillingPlan(payload) {
  return apiPost('/api/platform/billing/plans', payload)
}

export async function updatePlatformBillingPlan(id, payload) {
  return apiPut(`/api/platform/billing/plans/${id}`, payload)
}

export async function getPlatformCustomers(q = '') {
  const qs = new URLSearchParams()
  if (q) qs.set('q', q)
  const data = await apiGet(`/api/platform/customers${qs.toString() ? `?${qs.toString()}` : ''}`)
  return Array.isArray(data) ? data : (data?.items || [])
}

export async function getPlatformUsers(q = '') {
  const qs = new URLSearchParams()
  if (q) qs.set('q', q)
  return apiGet(`/api/platform/customers/users${qs.toString() ? `?${qs.toString()}` : ''}`)
}

export async function getPlatformUserTenants(userId) {
  return apiGet(`/api/platform/customers/user-tenants?userId=${encodeURIComponent(userId)}`)
}

export async function getPlatformCustomerDetails(tenantId) {
  return apiGet(`/api/platform/customers/${tenantId}`)
}

export async function getPlatformCustomerUsage(tenantId, month = '') {
  const qs = new URLSearchParams()
  if (month) qs.set('month', month)
  return apiGet(`/api/platform/customers/${tenantId}/usage${qs.toString() ? `?${qs.toString()}` : ''}`)
}

export async function getPlatformCustomerFeatures(tenantId) {
  return apiGet(`/api/platform/customers/${encodeURIComponent(tenantId)}/features`)
}

export async function getPlatformCustomerCompanySettings(tenantId) {
  return apiGet(`/api/platform/customers/${encodeURIComponent(tenantId)}/company-settings`)
}

export async function savePlatformCustomerCompanySettings(tenantId, payload) {
  return apiPut(`/api/platform/customers/${encodeURIComponent(tenantId)}/company-settings`, payload || {})
}

export async function savePlatformCustomerFeatures(tenantId, payload) {
  return apiPut(`/api/platform/customers/${encodeURIComponent(tenantId)}/features`, payload || {})
}

export async function getPlatformCustomerSubscriptions(tenantId) {
  return apiGet(`/api/platform/customers/${tenantId}/subscriptions`)
}

export async function getPlatformCustomerInvoices(tenantId) {
  return apiGet(`/api/platform/customers/${tenantId}/invoices`)
}

export async function getPlatformCustomerMembers(tenantId) {
  return apiGet(`/api/platform/customers/${tenantId}/members`)
}

export async function getPlatformCustomerActivity(tenantId, limit = 100) {
  return apiGet(`/api/platform/customers/${tenantId}/activity?limit=${encodeURIComponent(limit)}`)
}

export async function assignPlatformCustomerPlan(tenantId, payload) {
  return apiPost(`/api/platform/customers/${tenantId}/assign-plan`, payload)
}

export async function getPlatformPurchaseReport(filters = {}) {
  const qs = new URLSearchParams()
  if (filters.fromUtc) qs.set('fromUtc', filters.fromUtc)
  if (filters.toUtc) qs.set('toUtc', filters.toUtc)
  if (filters.service) qs.set('service', filters.service)
  if (filters.q) qs.set('q', filters.q)
  if (filters.status) qs.set('status', filters.status)
  if (filters.page) qs.set('page', String(filters.page))
  if (filters.pageSize) qs.set('pageSize', String(filters.pageSize))
  return apiGet(`/api/platform/purchases${qs.toString() ? `?${qs.toString()}` : ''}`)
}

export async function viewPlatformPurchaseInvoice(invoiceId) {
  return apiGetBlob(`/api/platform/purchases/${encodeURIComponent(invoiceId)}/view`)
}

export async function sendPlatformPurchaseInvoice(invoiceId) {
  return apiPost(`/api/platform/purchases/${encodeURIComponent(invoiceId)}/send`, {})
}

export async function getPlatformSupportTickets(filters = {}) {
  const q = new URLSearchParams()
  if (filters.status) q.set('status', filters.status)
  if (filters.service) q.set('service', filters.service)
  if (filters.q) q.set('q', filters.q)
  if (filters.tenantId) q.set('tenantId', filters.tenantId)
  if (filters.page) q.set('page', String(filters.page))
  if (filters.pageSize) q.set('pageSize', String(filters.pageSize))
  return apiGet(`/api/platform/support/tickets${q.toString() ? `?${q.toString()}` : ''}`)
}

export async function getPlatformSupportTicketDetails(ticketId) {
  return apiGet(`/api/platform/support/tickets/${encodeURIComponent(ticketId)}`)
}

export async function replyPlatformSupportTicket(ticketId, payload) {
  return apiPost(`/api/platform/support/tickets/${encodeURIComponent(ticketId)}/reply`, payload || {})
}

export async function updatePlatformSupportTicketStatus(ticketId, payload) {
  return apiPost(`/api/platform/support/tickets/${encodeURIComponent(ticketId)}/status`, payload || {})
}

export async function archivePlatformBillingPlan(id) {
  return apiDelete(`/api/platform/billing/plans/${id}`)
}

export async function getBillingPlans() {
  return apiGet('/api/billing/plans')
}

export async function getCurrentBillingPlan() {
  return apiGet('/api/billing/current-plan')
}

export async function getBillingUsage() {
  return apiGet('/api/billing/usage')
}

export async function getBillingDunningStatus() {
  return apiGet('/api/billing/dunning-status')
}

export async function getBillingInvoices() {
  return apiGet('/api/billing/invoices')
}

export async function changeBillingPlan(planCode, billingCycle = 'monthly') {
  return apiPost('/api/billing/change-plan', { planCode, billingCycle })
}

export async function cancelBillingSubscription() {
  return apiPost('/api/billing/cancel', {})
}

export async function getBillingPaymentConfig() {
  return apiGet('/api/billing/payment-config')
}

export async function createRazorpayOrder(planCode, billingCycle = 'monthly') {
  return apiPost('/api/billing/razorpay/create-order', { planCode, billingCycle })
}

export async function createIntegrationRazorpayOrder(slug) {
  return apiPost('/api/billing/razorpay/create-integration-order', { slug })
}

export async function verifyRazorpayPayment(payload) {
  return apiPost('/api/billing/razorpay/verify', payload)
}

export async function getIntegrationCatalog() {
  return apiGet('/api/integrations/catalog')
}

export async function getPlatformIntegrationCatalog() {
  return apiGet('/api/platform/integrations/catalog')
}

export async function savePlatformIntegrationCatalog(items = []) {
  return apiPut('/api/platform/integrations/catalog', items)
}

export async function getAuthenticatorStatus() {
  return apiGet('/api/security/authenticator')
}

export async function setupAuthenticator(provider) {
  return apiPost('/api/security/authenticator/setup', { provider })
}

export async function verifyAuthenticator(code) {
  return apiPost('/api/security/authenticator/verify', { code })
}

export async function disableAuthenticator() {
  return apiDelete('/api/security/authenticator')
}

export async function downloadBillingInvoice(invoiceId) {
  return apiGetBlob(`/api/billing/invoices/${encodeURIComponent(invoiceId)}/download`)
}

export async function downloadAllBillingInvoices() {
  return apiGetBlob('/api/billing/invoices/download-all')
}

export async function verifyBillingInvoice(invoiceId) {
  return apiGet(`/api/billing/invoices/${encodeURIComponent(invoiceId)}/verify`)
}

export async function exportBillingReconciliation(month = '') {
  const q = new URLSearchParams()
  if (month) q.set('month', month)
  const qs = q.toString()
  return apiGet(`/api/billing/reconciliation/export${qs ? `?${qs}` : ''}`)
}

export async function resyncBillingUsage() {
  return apiPost('/api/billing/usage/resync', {})
}

export async function getPublicPlans() {
  const res = await fetch(`${API_BASE}/api/public/plans`)
  if (!res.ok) throw new Error(`GET /api/public/plans failed (${res.status})`)
  return res.json()
}

export async function getCompanySettings() {
  return apiGet('/api/settings/company')
}

export async function saveCompanySettings(payload) {
  return apiPut('/api/settings/company', payload)
}

export async function getNotificationSettings() {
  return apiGet('/api/settings/notifications')
}

export async function saveNotificationSettings(payload) {
  return apiPut('/api/settings/notifications', payload)
}

export async function getSecurityOverview() {
  return apiGet('/api/settings/security')
}

export async function updateContactGroupById(id, payload) {
  return apiPut(`/api/contact-groups/${encodeURIComponent(id)}`, payload || {})
}

export async function deleteContactGroupById(id) {
  return apiDelete(`/api/contact-groups/${encodeURIComponent(id)}`)
}

export async function upsertContactCustomFields(contactId, payload) {
  return apiPost(`/api/contact-data/contacts/${encodeURIComponent(contactId)}/custom-fields`, payload || {})
}

export async function getWabaStatus() {
  return apiGet('/api/waba/status')
}

export async function getMessageMedia(mediaId) {
  return apiGet(`/api/messages/media/${encodeURIComponent(mediaId)}`)
}

// ---- Additional explicit API helpers (safe wiring, no behavior change) ----
export async function getAutomationNodeTypeCatalog() {
  return apiGet('/api/automation/catalogs/node-types')
}

export async function getAutomationFlowJsonSchema() {
  return apiGet('/api/automation/flow-json-schema')
}

export async function getAutomationFlow(flowId) {
  return apiGet(`/api/automation/flows/${encodeURIComponent(flowId)}`)
}

export async function getAutomationFlowVersions(flowId) {
  return apiGet(`/api/automation/flows/${encodeURIComponent(flowId)}/versions`)
}

export async function requestAutomationFlowApproval(flowId) {
  return apiPost(`/api/automation/flows/${encodeURIComponent(flowId)}/approvals/request`, {})
}

export async function decideAutomationFlowApproval(flowId, approvalId, payload) {
  return apiPost(`/api/automation/flows/${encodeURIComponent(flowId)}/approvals/${encodeURIComponent(approvalId)}/decide`, payload || {})
}

export async function publishAutomationFlowVersion(flowId, versionId, payload = {}) {
  return apiPost(`/api/automation/flows/${encodeURIComponent(flowId)}/versions/${encodeURIComponent(versionId)}/publish`, payload)
}

export async function rollbackAutomationFlowVersion(flowId, versionId, payload = {}) {
  return apiPost(`/api/automation/flows/${encodeURIComponent(flowId)}/versions/${encodeURIComponent(versionId)}/rollback`, payload)
}

export async function validateAutomationFlowVersion(flowId, versionId, payload = {}) {
  return apiPost(`/api/automation/flows/${encodeURIComponent(flowId)}/versions/${encodeURIComponent(versionId)}/validate`, payload)
}

export async function unpublishAutomationFlow(flowId) {
  return apiPost(`/api/automation/flows/${encodeURIComponent(flowId)}/unpublish`, {})
}

export async function addAutomationFlowNode(flowId, payload) {
  return apiPost(`/api/automation/flows/${encodeURIComponent(flowId)}/nodes`, payload || {})
}

export async function runAutomationFlow(flowId, payload) {
  return apiPost(`/api/automation/flows/${encodeURIComponent(flowId)}/run`, payload || {})
}

export async function simulateAutomationFlow(flowId, payload) {
  return apiPost(`/api/automation/flows/${encodeURIComponent(flowId)}/simulate`, payload || {})
}

export async function sendAutomationFlow(flowId, payload) {
  return apiPost(`/api/automation/flows/${encodeURIComponent(flowId)}/send-flow`, payload || {})
}

export async function importMetaFlowIntoAutomation(flowId, payload) {
  return apiPost(`/api/automation/flows/${encodeURIComponent(flowId)}/import-meta`, payload || {})
}

export async function getAutomationFlowRuns(flowId = '') {
  const q = new URLSearchParams()
  if (flowId) q.set('flowId', flowId)
  const qs = q.toString()
  return apiGet(`/api/automation/runs${qs ? `?${qs}` : ''}`)
}

export async function getAutomationFlowRun(runId) {
  return apiGet(`/api/automation/runs/${encodeURIComponent(runId)}`)
}

export async function getAutomationTriggerAudit(params = {}) {
  const q = new URLSearchParams()
  if (params.flowId) q.set('flowId', params.flowId)
  if (params.inboundMessageId) q.set('inboundMessageId', params.inboundMessageId)
  if (params.matched !== undefined && params.matched !== null && params.matched !== '') q.set('matched', String(params.matched))
  if (params.take) q.set('take', String(params.take))
  const qs = q.toString()
  return apiGet(`/api/automation/trigger-audit${qs ? `?${qs}` : ''}`)
}

export async function getAutomationMetricsFlows(days = 7) {
  return apiGet(`/api/automation/metrics/flows?days=${encodeURIComponent(String(days))}`)
}

export async function getAutomationMetricsFlowEvents(params = {}) {
  const q = new URLSearchParams()
  if (params.flowId) q.set('flowId', params.flowId)
  if (params.days) q.set('days', String(params.days))
  if (params.limit) q.set('limit', String(params.limit))
  const qs = q.toString()
  return apiGet(`/api/automation/metrics/flows/events${qs ? `?${qs}` : ''}`)
}

export async function getMetaFlow(metaFlowId) {
  return apiGet(`/api/automation/meta/flows/${encodeURIComponent(metaFlowId)}`)
}

export async function publishMetaFlow(metaFlowId, payload = {}) {
  return apiPost(`/api/automation/meta/flows/${encodeURIComponent(metaFlowId)}/publish`, payload)
}

export async function getConversationMessages(conversationId, { take = 80, cursor = '' } = {}) {
  const q = new URLSearchParams()
  if (take) q.set('take', String(take))
  if (cursor) q.set('cursor', cursor)
  return apiGet(`/api/inbox/conversations/${encodeURIComponent(conversationId)}/messages?${q.toString()}`)
}

export async function assignConversation(conversationId, payload) {
  return apiPost(`/api/inbox/conversations/${encodeURIComponent(conversationId)}/assign`, payload || {})
}

export async function transferConversation(conversationId, payload) {
  return apiPost(`/api/inbox/conversations/${encodeURIComponent(conversationId)}/transfer`, payload || {})
}

export async function updateConversationLabels(conversationId, labels = []) {
  return apiPost(`/api/inbox/conversations/${encodeURIComponent(conversationId)}/labels`, { labels })
}

export async function getConversationNotes(conversationId, take = 50) {
  return apiGet(`/api/inbox/conversations/${encodeURIComponent(conversationId)}/notes?take=${encodeURIComponent(String(take))}`)
}

export async function addConversationNote(conversationId, body) {
  return apiPost(`/api/inbox/conversations/${encodeURIComponent(conversationId)}/notes`, { body })
}

export async function updateTeamMemberRole(userId, role) {
  return apiPatch(`/api/team/members/${encodeURIComponent(userId)}/role`, { role })
}

export async function removeTeamMember(userId) {
  return apiDelete(`/api/team/members/${encodeURIComponent(userId)}`)
}

export async function getTeamMemberActivity(userId, limit = 50) {
  return apiGet(`/api/team/members/${encodeURIComponent(userId)}/activity?limit=${encodeURIComponent(String(limit))}`)
}

export async function getTeamMemberPermissions(userId) {
  return apiGet(`/api/team/members/${encodeURIComponent(userId)}/permissions`)
}

export async function updateTeamMemberPermissions(userId, overrides = []) {
  return apiPut(`/api/team/members/${encodeURIComponent(userId)}/permissions`, { overrides })
}

export async function cancelTeamInvitation(email) {
  return apiPost('/api/team/invitations/cancel', { email })
}

export async function templateLifecycleSubmit(id) {
  return apiPost(`/api/template-lifecycle/${encodeURIComponent(id)}/submit`, {})
}

export async function templateLifecycleApprove(id) {
  return apiPost(`/api/template-lifecycle/${encodeURIComponent(id)}/approve`, {})
}

export async function templateLifecycleReject(id) {
  return apiPost(`/api/template-lifecycle/${encodeURIComponent(id)}/reject`, {})
}

export async function templateLifecycleDisable(id) {
  return apiPost(`/api/template-lifecycle/${encodeURIComponent(id)}/disable`, {})
}

export async function templateLifecycleNewVersion(id) {
  return apiPost(`/api/template-lifecycle/${encodeURIComponent(id)}/version`, {})
}

export async function updateCampaignById(id, payload) {
  return apiPut(`/api/campaigns/${encodeURIComponent(id)}`, payload || {})
}

export async function deleteCampaignById(id) {
  return apiDelete(`/api/campaigns/${encodeURIComponent(id)}`)
}

export async function updateContactById(id, payload) {
  return apiPut(`/api/contacts/${encodeURIComponent(id)}`, payload || {})
}

export async function deleteContactById(id) {
  return apiDelete(`/api/contacts/${encodeURIComponent(id)}`)
}
