import { useQuery } from '@tanstack/react-query'
import { api } from '@/lib/api/client'
import type { DashboardDto } from '@/lib/api/types'

export type DashboardParams = { days: 7 | 30 | 90; warehouseId?: string }

export function useDashboard(params: DashboardParams) {
  return useQuery({
    // Under ['inventory'] too, so every stock, purchase or sales write (which invalidates 'inventory') refreshes it.
    queryKey: ['inventory', 'dashboard', params],
    queryFn: ({ signal }) => api.get<DashboardDto>('/dashboard', { params, signal }).then((r) => r.data),
    placeholderData: (previous) => previous,
  })
}
