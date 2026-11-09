import { api } from '@/lib/api/client'
import type { AccessTokenResponse, MeResponse, RegisterCompanyCommand, RegisterCompanyResult, SessionDto } from '@/lib/api/types'

export const authApi = {
  login: (body: { email: string; password: string }) =>
    api.post<AccessTokenResponse>('/auth/login', body).then((r) => r.data),

  register: (body: RegisterCompanyCommand) =>
    api.post<RegisterCompanyResult>('/auth/register', body).then((r) => r.data),

  me: () => api.get<MeResponse>('/auth/me').then((r) => r.data),

  logout: () => api.post('/auth/logout'),
  logoutAll: () => api.post('/auth/logout-all'),

  verifyEmail: (token: string) => api.post('/auth/verify-email', { token }),
  resendVerification: (email: string) => api.post('/auth/resend-verification', { email }),
  forgotPassword: (email: string) => api.post('/auth/forgot-password', { email }),
  resetPassword: (body: { token: string; newPassword: string }) => api.post('/auth/reset-password', body),

  sessions: () => api.get<SessionDto[]>('/auth/sessions').then((r) => r.data),
  revokeSession: (id: string) => api.delete(`/auth/sessions/${id}`),
}
