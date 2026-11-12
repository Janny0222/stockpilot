import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/lib/api/client'
import type { NotificationDto, NotificationPreferenceDto, NotificationType, PagedResult } from '@/lib/api/types'
import { useAuthStore } from '@/stores/auth-store'
import { useLiveStatus } from './live-status'

export type NotificationParams = { page?: number; pageSize?: number; unreadOnly?: boolean }

export const notificationKeys = {
  all: ['notifications'] as const,
  unread: ['notifications', 'unread-count'] as const,
  list: (p: NotificationParams) => ['notifications', 'list', p] as const,
  preferences: ['notifications', 'preferences'] as const,
}

/** How often the bell asks "anything new?". Only while the tab is visible (TanStack pauses intervals in the background). */
export const UNREAD_POLL_MS = 60_000

/** The unread count behind the bell: refreshed by live pushes, with polling as the fallback. */
export function useUnreadCount() {
  const hasTenant = useAuthStore((s) => !!s.user?.tenant)
  const live = useLiveStatus((s) => s.connected)
  return useQuery({
    queryKey: notificationKeys.unread,
    queryFn: ({ signal }) => api.get<{ count: number }>('/notifications/unread-count', { signal }).then((r) => r.data.count),
    enabled: hasTenant,
    // Live channel up → pushes refresh the count, no need to ask. Down (sleeping server, proxy without WebSockets,
    // offline) → poll every minute, so the bell is never more than a minute stale.
    refetchInterval: live ? false : UNREAD_POLL_MS,
  })
}

export function useNotifications(params: NotificationParams, enabled = true) {
  return useQuery({
    queryKey: notificationKeys.list(params),
    queryFn: ({ signal }) => api.get<PagedResult<NotificationDto>>('/notifications', { params, signal }).then((r) => r.data),
    placeholderData: (previous) => previous,
    enabled,
  })
}

export function useMarkRead() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api.post(`/notifications/${id}/read`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: notificationKeys.all }),
  })
}

export function useMarkAllRead() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => api.post<{ marked: number }>('/notifications/read-all').then((r) => r.data),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: notificationKeys.all }),
  })
}

export function useNotificationPreferences() {
  return useQuery({
    queryKey: notificationKeys.preferences,
    queryFn: ({ signal }) => api.get<NotificationPreferenceDto[]>('/notifications/preferences', { signal }).then((r) => r.data),
  })
}

export function useUpdateNotificationPreference() {
  const queryClient = useQueryClient()
  return useMutation({
    // Send only the switch that changed; the API leaves the other one as it is.
    mutationFn: (pref: { type: NotificationType; inApp?: boolean; email?: boolean }) =>
      api.put<NotificationPreferenceDto[]>('/notifications/preferences', { preferences: [pref] }).then((r) => r.data),
    onSuccess: (data) => queryClient.setQueryData(notificationKeys.preferences, data),
  })
}
