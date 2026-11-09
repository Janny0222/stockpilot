import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/lib/api/client'
import type { PageParams, PagedResult, SaveWarehouseCommand, WarehouseDto } from '@/lib/api/types'

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

/** Active warehouses the user can work with: feeds the selects in stock forms (≤100 per company). */
export function useActiveWarehouses() {
  return useWarehouses({ page: 1, pageSize: 100, isActive: true })
}

function useInvalidateWarehouses() {
  const queryClient = useQueryClient()
  return () => queryClient.invalidateQueries({ queryKey: warehouseKeys.all })
}

export function useSaveWarehouse() {
  const invalidate = useInvalidateWarehouses()
  return useMutation({
    mutationFn: ({ id, body }: { id?: string; body: SaveWarehouseCommand }) =>
      (id ? api.put<WarehouseDto>(`/warehouses/${id}`, body) : api.post<WarehouseDto>('/warehouses', body)).then((r) => r.data),
    onSuccess: invalidate,
  })
}

export function useWarehouseAction(id: string) {
  const invalidate = useInvalidateWarehouses()
  return useMutation({
    mutationFn: (action: 'archive' | 'restore' | 'default') => api.post(`/warehouses/${id}/${action}`),
    onSuccess: invalidate,
  })
}
