import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from 'react-router'
import { refreshAccessToken } from '@/lib/api/client'
import { useAuthStore } from '@/stores/auth-store'
import { authApi } from './api'

/**
 * Runs once at app start. A page reload wipes the in-memory access token, but the HttpOnly refresh
 * cookie survives, so we silently exchange it for a new token and load the profile.
 * No cookie (or an expired one) → "unauthenticated", and the router shows the login page.
 */
export async function bootstrapSession() {
  const token = await refreshAccessToken()
  if (!token) return

  try {
    useAuthStore.getState().setUser(await authApi.me())
  } catch {
    useAuthStore.getState().clear()
  }
}

/** Signs in, then loads the profile (permissions drive the navigation). */
export async function signIn(email: string, password: string) {
  const { accessToken } = await authApi.login({ email, password })
  useAuthStore.getState().setAccessToken(accessToken)
  useAuthStore.getState().setUser(await authApi.me())
}

export function useSignOut() {
  const queryClient = useQueryClient()
  const navigate = useNavigate()

  return useMutation({
    mutationFn: async (everywhere: boolean = false) => {
      // Even if the network call fails, the local session must end.
      await (everywhere ? authApi.logoutAll() : authApi.logout()).catch(() => undefined)
    },
    onSettled: () => {
      useAuthStore.getState().clear('signout')
      queryClient.clear() // never show the previous user's cached data to the next one
      navigate('/login', { replace: true })
    },
  })
}
