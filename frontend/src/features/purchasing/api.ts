import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/lib/api/client'
import type {
  PageParams,
  PagedResult,
  PurchaseOrderDetailDto,
  PurchaseOrderListItemDto,
  PurchaseOrderStatus,
  ReceiptResult,
  ReceivePurchaseOrderCommand,
  SavePurchaseOrderCommand,
  SaveSupplierCommand,
  SupplierDto,
} from '@/lib/api/types'

export type SupplierParams = PageParams & { isActive?: boolean }
export type PurchaseOrderParams = PageParams & {
  status?: PurchaseOrderStatus
  supplierId?: string
  warehouseId?: string
}

export const supplierKeys = {
  all: ['suppliers'] as const,
  list: (p: SupplierParams) => [...supplierKeys.all, 'list', p] as const,
  detail: (id: string) => [...supplierKeys.all, 'detail', id] as const,
}

export const purchaseKeys = {
  all: ['purchase-orders'] as const,
  list: (p: PurchaseOrderParams) => [...purchaseKeys.all, 'list', p] as const,
  detail: (id: string) => [...purchaseKeys.all, 'detail', id] as const,
}

// ---------------- Suppliers ----------------

export function useSuppliers(params: SupplierParams) {
  return useQuery({
    queryKey: supplierKeys.list(params),
    queryFn: ({ signal }) => api.get<PagedResult<SupplierDto>>('/suppliers', { params, signal }).then((r) => r.data),
    placeholderData: (previous) => previous,
  })
}

export function useSupplier(id: string) {
  return useQuery({
    queryKey: supplierKeys.detail(id),
    queryFn: ({ signal }) => api.get<SupplierDto>(`/suppliers/${id}`, { signal }).then((r) => r.data),
  })
}

export function useSaveSupplier() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id?: string; body: SaveSupplierCommand }) =>
      id ? api.put(`/suppliers/${id}`, body).then(() => id) : api.post<{ id: string }>('/suppliers', body).then((r) => r.data.id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: supplierKeys.all }),
  })
}

export function useSetSupplierArchived(id: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (archive: boolean) => api.post(`/suppliers/${id}/${archive ? 'archive' : 'restore'}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: supplierKeys.all }),
  })
}

// ---------------- Purchase orders ----------------

export function usePurchaseOrders(params: PurchaseOrderParams) {
  return useQuery({
    queryKey: purchaseKeys.list(params),
    queryFn: ({ signal }) =>
      api
        .get<PagedResult<PurchaseOrderListItemDto>>('/purchase-orders', {
          params,
          signal,
        })
        .then((r) => r.data),
    placeholderData: (previous) => previous,
  })
}

export function usePurchaseOrder(id: string, enabled = true) {
  return useQuery({
    queryKey: purchaseKeys.detail(id),
    queryFn: ({ signal }) => api.get<PurchaseOrderDetailDto>(`/purchase-orders/${id}`, { signal }).then((r) => r.data),
    enabled,
  })
}

/** Writes to a PO also change supplier stats and, for receipts, stock: refresh all three. */
function useInvalidatePurchasing() {
  const queryClient = useQueryClient()
  return () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: purchaseKeys.all }),
      queryClient.invalidateQueries({ queryKey: supplierKeys.all }),
      queryClient.invalidateQueries({ queryKey: ['inventory'] }),
    ])
}

export function useSavePurchaseOrder() {
  const invalidate = useInvalidatePurchasing()
  return useMutation({
    mutationFn: ({ id, body }: { id?: string; body: SavePurchaseOrderCommand }) =>
      id ? api.put(`/purchase-orders/${id}`, body).then(() => id) : api.post<{ id: string }>('/purchase-orders', body).then((r) => r.data.id),
    onSuccess: invalidate,
  })
}

export function usePurchaseOrderAction(id: string) {
  const invalidate = useInvalidatePurchasing()
  return useMutation({
    mutationFn: (action: { type: 'submit' | 'approve' } | { type: 'cancel'; reason: string }) =>
      action.type === 'cancel' ? api.post(`/purchase-orders/${id}/cancel`, { reason: action.reason }) : api.post(`/purchase-orders/${id}/${action.type}`),
    onSuccess: invalidate,
  })
}

export function useReceivePurchaseOrder(id: string) {
  const invalidate = useInvalidatePurchasing()
  return useMutation({
    mutationFn: ({ body, idempotencyKey }: { body: ReceivePurchaseOrderCommand; idempotencyKey: string }) =>
      api
        .post<ReceiptResult>(`/purchase-orders/${id}/receipts`, body, {
          headers: { 'Idempotency-Key': idempotencyKey },
        })
        .then((r) => r.data),
    onSuccess: invalidate,
  })
}

export const PO_STATUS: Record<PurchaseOrderStatus, { label: string; className: string }> = {
  Draft: { label: 'Draft', className: 'text-muted-foreground' },
  Submitted: {
    label: 'Awaiting approval',
    className: 'border-info/30 bg-info/10 text-info',
  },
  Approved: {
    label: 'Approved',
    className: 'border-primary/30 bg-secondary text-secondary-foreground',
  },
  PartiallyReceived: {
    label: 'Partially received',
    className: 'border-warning/40 bg-warning/10 text-warning',
  },
  Received: {
    label: 'Received',
    className: 'border-success/30 bg-success/10 text-success',
  },
  Cancelled: {
    label: 'Cancelled',
    className: 'text-muted-foreground line-through',
  },
}
