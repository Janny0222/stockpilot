import axios, { type AxiosError, type InternalAxiosRequestConfig } from 'axios'
import { useAuthStore } from '@/stores/auth-store'
import type { AccessTokenResponse } from './types'

/**
 * Data flow for every request:
 *   component → TanStack Query → api (this axios instance)
 *     → request interceptor adds "Authorization: Bearer <token from memory>"
 *     → Vite dev proxy (prod: Cloudflare Pages Function) → ASP.NET API
 *     ← 401? response interceptor refreshes ONCE via the HttpOnly cookie, then replays the request.
 *
 * Same-origin "/api" base URL means the browser sends the SameSite=Strict refresh cookie automatically.
 */
export const api = axios.create({
  baseURL: '/api/v1',
  withCredentials: true,
  headers: { 'X-Requested-With': 'XMLHttpRequest' },
})

/** A bare client for the refresh call itself, so it can never trigger the 401 → refresh loop. */
const authClient = axios.create({ baseURL: '/api/v1', withCredentials: true })

api.interceptors.request.use((config) => {
  const token = useAuthStore.getState().accessToken
  if (token) config.headers.Authorization = `Bearer ${token}`
  return config
})

let refreshInFlight: Promise<string | null> | null = null

/**
 * Single-flight refresh: if five requests fail with 401 at once, they all await the SAME refresh
 * call. Five parallel refreshes would rotate the token five times, and the server would treat
 * the stale ones as a replay attack and revoke the whole session.
 */
export function refreshAccessToken(): Promise<string | null> {
  refreshInFlight ??= authClient
    .post<AccessTokenResponse>('/auth/refresh')
    .then(({ data }) => {
      useAuthStore.getState().setAccessToken(data.accessToken)
      return data.accessToken
    })
    .catch(() => {
      useAuthStore.getState().clear()
      return null
    })
    .finally(() => {
      refreshInFlight = null
    })

  return refreshInFlight
}

type RetriableConfig = InternalAxiosRequestConfig & { _retried?: boolean }

// Credential endpoints answer 401 for "wrong password", not "token expired": never refresh for those.
const NO_REFRESH = ['/auth/login', '/auth/refresh', '/auth/register', '/auth/logout']

api.interceptors.response.use(undefined, async (error: AxiosError) => {
  const original = error.config as RetriableConfig | undefined
  const isAuthEndpoint = NO_REFRESH.some((path) => original?.url?.startsWith(path))

  if (error.response?.status !== 401 || !original || original._retried || isAuthEndpoint) {
    throw error
  }

  original._retried = true
  const token = await refreshAccessToken()
  if (!token) throw error

  original.headers.Authorization = `Bearer ${token}`
  return api(original)
})
