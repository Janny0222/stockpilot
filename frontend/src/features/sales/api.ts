import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/lib/api/client'
import type {
  CustomerDto,
  PageParams,
  PagedResult,
  SalesOrderDetailDto,
  SalesOrderListItemDto,
  SalesOrderStatus,
  SaveCustomerCommand,
  SaveSalesOrderCommand,
  ShipmentResult,
  ShipSalesOrderCommand,
} from '@/lib/api/types'

export type CustomerParams = PageParams & { isActive?: boolean }
export type SalesOrderParams = PageParams & { status?: SalesOrderStatus; customerId?: string; warehouseId?: string }

export const customerKeys = {
  all: ['customers'] as const,
  list: (p: CustomerParams) => [...customerKeys.all, 'list', p] as const,
  detail: (id: string) => [...customerKeys.all, 'detail', id] as const,
}

export const salesKeys = {
  all: ['sales-orders'] as const,
  list: (p: SalesOrderParams) => [...salesKeys.all, 'list', p] as const,
  detail: (id: string) => [...salesKeys.all, 'detail', id] as const,
}

// ---------------- Customers ----------------

export function useCustomers(params: CustomerParams) {
  return useQuery({
    queryKey: customerKeys.list(params),
    queryFn: ({ signal }) => api.get<PagedResult<CustomerDto>>('/customers', { params, signal }).then((r) => r.data),
    placeholderData: (previous) => previous,
  })
}

export function useCustomer(id: string) {
  return useQuery({
    queryKey: customerKeys.detail(id),
    queryFn: ({ signal }) => api.get<CustomerDto>(`/customers/${id}`, { signal }).then((r) => r.data),
  })
}

export function useSaveCustomer() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id?: string; body: SaveCustomerCommand }) =>
      id ? api.put(`/customers/${id}`, body).then(() => id) : api.post<{ id: string }>('/customers', body).then((r) => r.data.id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: customerKeys.all }),
  })
}

export function useSetCustomerArchived(id: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (archive: boolean) => api.post(`/customers/${id}/${archive ? 'archive' : 'restore'}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: customerKeys.all }),
  })
}

// ---------------- Sales orders ----------------

export function useSalesOrders(params: SalesOrderParams) {
  return useQuery({
    queryKey: salesKeys.list(params),
    queryFn: ({ signal }) => api.get<PagedResult<SalesOrderListItemDto>>('/sales-orders', { params, signal }).then((r) => r.data),
    placeholderData: (previous) => previous,
  })
}

export function useSalesOrder(id: string, enabled = true) {
  return useQuery({
    queryKey: salesKeys.detail(id),
    queryFn: ({ signal }) => api.get<SalesOrderDetailDto>(`/sales-orders/${id}`, { signal }).then((r) => r.data),
    enabled,
  })
}

/** Confirming, shipping and cancelling all move reservations or stock: refresh orders, customers and inventory. */
function useInvalidateSales() {
  const queryClient = useQueryClient()
  return () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: salesKeys.all }),
      queryClient.invalidateQueries({ queryKey: customerKeys.all }),
      queryClient.invalidateQueries({ queryKey: ['inventory'] }),
    ])
}

export function useSaveSalesOrder() {
  const invalidate = useInvalidateSales()
  return useMutation({
    mutationFn: ({ id, body }: { id?: string; body: SaveSalesOrderCommand }) =>
      id ? api.put(`/sales-orders/${id}`, body).then(() => id) : api.post<{ id: string }>('/sales-orders', body).then((r) => r.data.id),
    onSuccess: invalidate,
  })
}

export function useSalesOrderAction(id: string) {
  const invalidate = useInvalidateSales()
  return useMutation({
    mutationFn: (action: { type: 'confirm' } | { type: 'cancel'; reason: string }) =>
      action.type === 'cancel' ? api.post(`/sales-orders/${id}/cancel`, { reason: action.reason }) : api.post(`/sales-orders/${id}/confirm`),
    onSuccess: invalidate,
  })
}

export function useShipSalesOrder(id: string) {
  const invalidate = useInvalidateSales()
  return useMutation({
    mutationFn: ({ body, idempotencyKey }: { body: ShipSalesOrderCommand; idempotencyKey: string }) =>
      api.post<ShipmentResult>(`/sales-orders/${id}/shipments`, body, { headers: { 'Idempotency-Key': idempotencyKey } }).then((r) => r.data),
    onSuccess: invalidate,
  })
}

export const SO_STATUS: Record<SalesOrderStatus, { label: string; className: string }> = {
  Draft: { label: 'Draft', className: 'text-muted-foreground' },
  Confirmed: { label: 'Confirmed', className: 'border-primary/30 bg-secondary text-secondary-foreground' },
  PartiallyFulfilled: { label: 'Partially shipped', className: 'border-warning/40 bg-warning/10 text-warning' },
  Fulfilled: { label: 'Fulfilled', className: 'border-success/30 bg-success/10 text-success' },
  Cancelled: { label: 'Cancelled', className: 'text-muted-foreground line-through' },
}
