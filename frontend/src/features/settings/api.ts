import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { authApi } from '@/features/auth/api'
import { api } from '@/lib/api/client'
import type {
  AuditFiltersDto,
  AuditLogDto,
  CompanySettingsDto,
  InviteUserCommand,
  PageParams,
  PagedResult,
  RolesOverviewDto,
  UpdateCompanySettingsCommand,
  UpdateUserCommand,
  UserDto,
  UserStatus,
} from '@/lib/api/types'
import { useAuthStore } from '@/stores/auth-store'

export type UserParams = PageParams & { status?: UserStatus; roleId?: string }
export type AuditParams = PageParams & { entityType?: string; action?: string; userId?: string; from?: string; to?: string }

export const settingsKeys = {
  company: ['settings', 'company'] as const,
  users: ['users'] as const,
  userList: (p: UserParams) => [...settingsKeys.users, 'list', p] as const,
  user: (id: string) => [...settingsKeys.users, 'detail', id] as const,
  roles: ['roles'] as const,
  audit: ['audit-logs'] as const,
  auditList: (p: AuditParams) => [...settingsKeys.audit, 'list', p] as const,
  auditFilters: ['audit-logs', 'filters'] as const,
}

// ---------------- Company ----------------

export function useCompanySettings() {
  return useQuery({
    queryKey: settingsKeys.company,
    queryFn: ({ signal }) => api.get<CompanySettingsDto>('/settings/company', { signal }).then((r) => r.data),
  })
}

/** Saving also reloads the profile: the company name, currency and time zone in /auth/me drive the whole app. */
export function useUpdateCompanySettings() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (body: UpdateCompanySettingsCommand) => api.put<CompanySettingsDto>('/settings/company', body).then((r) => r.data),
    onSuccess: async (data) => {
      queryClient.setQueryData(settingsKeys.company, data)
      useAuthStore.getState().setUser(await authApi.me())
      await queryClient.invalidateQueries({ queryKey: settingsKeys.audit })
    },
  })
}

// ---------------- Users ----------------

export function useUsers(params: UserParams) {
  return useQuery({
    queryKey: settingsKeys.userList(params),
    queryFn: ({ signal }) => api.get<PagedResult<UserDto>>('/users', { params, signal }).then((r) => r.data),
    placeholderData: (previous) => previous,
  })
}

export function useUser(id: string) {
  return useQuery({
    queryKey: settingsKeys.user(id),
    queryFn: ({ signal }) => api.get<UserDto>(`/users/${id}`, { signal }).then((r) => r.data),
  })
}

export function useRoles() {
  return useQuery({
    queryKey: settingsKeys.roles,
    queryFn: ({ signal }) => api.get<RolesOverviewDto>('/roles', { signal }).then((r) => r.data),
    staleTime: 5 * 60_000,
  })
}

function useInvalidateUsers() {
  const queryClient = useQueryClient()
  return () => Promise.all([queryClient.invalidateQueries({ queryKey: settingsKeys.users }), queryClient.invalidateQueries({ queryKey: settingsKeys.audit })])
}

export function useInviteUser() {
  const invalidate = useInvalidateUsers()
  return useMutation({
    mutationFn: (body: InviteUserCommand) => api.post<UserDto>('/users', body).then((r) => r.data),
    onSuccess: invalidate,
  })
}

export function useUpdateUser(id: string) {
  const invalidate = useInvalidateUsers()
  return useMutation({
    mutationFn: (body: UpdateUserCommand) => api.put<UserDto>(`/users/${id}`, body).then((r) => r.data),
    onSuccess: async (user) => {
      await invalidate()
      // Renaming yourself shows up in the sidebar straight away.
      if (user.isYou) useAuthStore.getState().setUser(await authApi.me())
    },
  })
}

export function useUserAction(id: string) {
  const invalidate = useInvalidateUsers()
  return useMutation({
    mutationFn: (action: 'deactivate' | 'activate' | 'resend-invitation') => api.post(`/users/${id}/${action}`),
    onSuccess: invalidate,
  })
}

// ---------------- Audit log ----------------

export function useAuditLogs(params: AuditParams) {
  return useQuery({
    queryKey: settingsKeys.auditList(params),
    queryFn: ({ signal }) => api.get<PagedResult<AuditLogDto>>('/audit-logs', { params, signal }).then((r) => r.data),
    placeholderData: (previous) => previous,
  })
}

export function useAuditFilters() {
  return useQuery({
    queryKey: settingsKeys.auditFilters,
    queryFn: ({ signal }) => api.get<AuditFiltersDto>('/audit-logs/filters', { signal }).then((r) => r.data),
  })
}

export const USER_STATUS: Record<UserStatus, { label: string; className: string }> = {
  Active: { label: 'Active', className: 'border-success/30 text-success' },
  Invited: { label: 'Invited', className: 'border-primary/30 bg-secondary text-secondary-foreground' },
  Deactivated: { label: 'Deactivated', className: 'text-muted-foreground' },
}
