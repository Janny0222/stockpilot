import { create } from 'zustand'
import type { MeResponse } from '@/lib/api/types'

export type AuthStatus = 'loading' | 'authenticated' | 'unauthenticated'

type AuthState = {
  /** Short-lived JWT. Kept in memory ONLY (never localStorage), so a script injected via XSS can't persist or exfiltrate it later. */
  accessToken: string | null
  user: MeResponse | null
  status: AuthStatus
  /** Why the session ended: a deliberate sign-out shouldn't send the NEXT user back to the previous user's page. */
  endedBy: 'signout' | 'expired' | null
  setAccessToken: (token: string | null) => void
  setUser: (user: MeResponse) => void
  clear: (reason?: 'signout' | 'expired') => void
}

/**
 * Global client state for "who is signed in". Server data (lists, details) lives in TanStack Query,
 * not here. Zustand holds only what the whole app needs synchronously.
 */
export const useAuthStore = create<AuthState>()((set) => ({
  accessToken: null,
  user: null,
  status: 'loading',
  endedBy: null,
  setAccessToken: (accessToken) => set({ accessToken }),
  setUser: (user) => set({ user, status: 'authenticated', endedBy: null }),
  clear: (reason = 'expired') => set({ accessToken: null, user: null, status: 'unauthenticated', endedBy: reason }),
}))

/** True when the signed-in user's role grants the permission. UX only: the API enforces it. */
export function useHasPermission(permission: string | undefined) {
  return useAuthStore((s) => !permission || (s.user?.permissions.includes(permission) ?? false))
}
