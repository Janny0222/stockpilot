import { useQuery } from '@tanstack/react-query'
import { api } from '@/lib/api/client'
import type { PageParams, PagedResult, WarehouseDto } from '@/lib/api/types'

export type WarehouseListParams = PageParams & { isActive?: boolean }

/**
 * Query keys are hierarchical: ['warehouses'] invalidates every warehouse query at once (after a
 * create/edit), while ['warehouses', 'list', params] caches each page/sort/search combination.
 */
export const warehouseKeys = {
  all: ['warehouses'] as const,
  list: (params: WarehouseListParams) => [...warehouseKeys.all, 'list', params] as const,
  detail: (id: string) => [...warehouseKeys.all, 'detail', id] as const,
}

export function useWarehouses(params: WarehouseListParams) {
  return useQuery({
    queryKey: warehouseKeys.list(params),
    queryFn: ({ signal }) => api.get<PagedResult<WarehouseDto>>('/warehouses', { params, signal }).then((r) => r.data),
    placeholderData: (previous) => previous, // keep showing page 1 while page 2 loads: no flicker
  })
}

export function useWarehouse(id: string) {
  return useQuery({
    queryKey: warehouseKeys.detail(id),
    queryFn: ({ signal }) => api.get<WarehouseDto>(`/warehouses/${id}`, { signal }).then((r) => r.data),
  })
}
