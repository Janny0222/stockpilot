import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/lib/api/client'
import type { JobRunOutcome, JobStatusDto, PageParams, PagedResult, PlatformMetricsDto, TenantStatus, TenantSummaryDto } from '@/lib/api/types'

export type TenantParams = PageParams & { status?: TenantStatus }

export const platformKeys = {
  all: ['platform'] as const,
  metrics: ['platform', 'metrics'] as const,
  list: (p: TenantParams) => ['platform', 'tenants', p] as const,
  detail: (id: string) => ['platform', 'tenant', id] as const,
}

export function usePlatformMetrics() {
  return useQuery({
    queryKey: platformKeys.metrics,
    queryFn: ({ signal }) => api.get<PlatformMetricsDto>('/platform/metrics', { signal }).then((r) => r.data),
  })
}

export function useTenants(params: TenantParams) {
  return useQuery({
    queryKey: platformKeys.list(params),
    queryFn: ({ signal }) => api.get<PagedResult<TenantSummaryDto>>('/platform/tenants', { params, signal }).then((r) => r.data),
    placeholderData: (previous) => previous,
  })
}

export function useTenant(id: string) {
  return useQuery({
    queryKey: platformKeys.detail(id),
    queryFn: ({ signal }) => api.get<TenantSummaryDto>(`/platform/tenants/${id}`, { signal }).then((r) => r.data),
  })
}

export function useJobs() {
  return useQuery({
    queryKey: [...platformKeys.all, 'jobs'] as const,
    queryFn: ({ signal }) => api.get<JobStatusDto[]>('/platform/jobs', { signal }).then((r) => r.data),
  })
}

/** Runs a job now and waits for it (they take seconds), then refreshes the status table. */
export function useRunJob() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (name: string) => api.post<JobRunOutcome>(`/platform/jobs/${name}/run`).then((r) => r.data),
    onSettled: () => queryClient.invalidateQueries({ queryKey: [...platformKeys.all, 'jobs'] }),
  })
}

export function useSetTenantStatus(id: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (action: { suspend: true; reason: string } | { suspend: false }) =>
      (action.suspend
        ? api.post<TenantSummaryDto>(`/platform/tenants/${id}/suspend`, { reason: action.reason })
        : api.post<TenantSummaryDto>(`/platform/tenants/${id}/activate`)
      ).then((r) => r.data),
    onSuccess: (data) => {
      queryClient.setQueryData(platformKeys.detail(id), data)
      return queryClient.invalidateQueries({ queryKey: platformKeys.all })
    },
  })
}
