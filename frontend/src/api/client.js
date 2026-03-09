import {
  apiRequest as libApiRequest,
  apiGet as libApiGet,
  apiPost as libApiPost,
  apiPostForm as libApiPostForm,
  authLogin as libAuthLogin,
  hasPermission as libHasPermission,
  wabaGetOnboardingStatus,
  wabaExchangeCode,
  wabaGetEmbeddedConfig
} from '../lib/api'

// Compatibility shim: keep existing imports working while using a single client implementation.
export function configureApiClient() {}

export const apiRequest = libApiRequest
export const apiGet = libApiGet
export const apiPost = libApiPost
export const apiPostForm = libApiPostForm
export const authLogin = libAuthLogin
export const hasPermission = libHasPermission

export async function getWabaStatus() {
  return wabaGetOnboardingStatus({ force: true })
}

export const exchangeEmbeddedSignupCode = wabaExchangeCode
export const getEmbeddedSignupConfig = wabaGetEmbeddedConfig
