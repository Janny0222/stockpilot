import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/lib/api/client'
import type {
  AdjustStockCommand,
  InventoryMovementType,
  MovementDto,
  PageParams,
  PagedResult,
  RecordStockCommand,
  StockLevelDto,
  StockOperationResult,
  TransferDetailDto,
  TransferListItemDto,
  TransferStockCommand,
  VariantOptionDto,
} from '@/lib/api/types'

export type StockParams = PageParams & { warehouseId?: string; categoryId?: string; lowStockOnly?: boolean }
export type MovementParams = PageParams & { warehouseId?: string; variantId?: string; productId?: string; type?: InventoryMovementType }

export const inventoryKeys = {
  all: ['inventory'] as const,
  stock: (p: StockParams) => [...inventoryKeys.all, 'stock', p] as const,
  movements: (p: MovementParams) => [...inventoryKeys.all, 'movements', p] as const,
  transfers: (p: PageParams) => [...inventoryKeys.all, 'transfers', p] as const,
  transfer: (id: string) => [...inventoryKeys.all, 'transfer', id] as const,
  variants: (search: string, warehouseId?: string) => [...inventoryKeys.all, 'variants', search, warehouseId] as const,
}

export function useStock(params: StockParams) {
  return useQuery({
    queryKey: inventoryKeys.stock(params),
    queryFn: ({ signal }) => api.get<PagedResult<StockLevelDto>>('/inventory/stock', { params, signal }).then((r) => r.data),
    placeholderData: (previous) => previous,
  })
}

export function useMovements(params: MovementParams) {
  return useQuery({
    queryKey: inventoryKeys.movements(params),
    queryFn: ({ signal }) => api.get<PagedResult<MovementDto>>('/inventory/movements', { params, signal }).then((r) => r.data),
    placeholderData: (previous) => previous,
  })
}

export function useTransfers(params: PageParams) {
  return useQuery({
    queryKey: inventoryKeys.transfers(params),
    queryFn: ({ signal }) => api.get<PagedResult<TransferListItemDto>>('/transfers', { params, signal }).then((r) => r.data),
    placeholderData: (previous) => previous,
  })
}

export function useTransfer(id: string) {
  return useQuery({
    queryKey: inventoryKeys.transfer(id),
    queryFn: ({ signal }) => api.get<TransferDetailDto>(`/transfers/${id}`, { signal }).then((r) => r.data),
  })
}

export function useVariantOptions(search: string, warehouseId: string | undefined, enabled: boolean) {
  return useQuery({
    queryKey: inventoryKeys.variants(search, warehouseId),
    queryFn: ({ signal }) =>
      api.get<VariantOptionDto[]>('/inventory/variants', { params: { search: search || undefined, warehouseId }, signal }).then((r) => r.data),
    enabled,
    staleTime: 10_000,
  })
}

/**
 * Every stock operation sends an Idempotency-Key (see useIdempotencyKey) and, on success, refreshes all
 * inventory screens AND warehouse/product views that show stock.
 */
function useStockMutation<TBody>(url: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ body, idempotencyKey }: { body: TBody; idempotencyKey: string }) =>
      api.post<StockOperationResult>(url, body, { headers: { 'Idempotency-Key': idempotencyKey } }).then((r) => r.data),
    onSuccess: () => Promise.all([
      queryClient.invalidateQueries({ queryKey: inventoryKeys.all }),
      queryClient.invalidateQueries({ queryKey: ['warehouses'] }),
    ]),
  })
}

export const useStockIn = () => useStockMutation<RecordStockCommand>('/inventory/stock-in')
export const useStockOut = () => useStockMutation<RecordStockCommand>('/inventory/stock-out')
export const useAdjustStock = () => useStockMutation<AdjustStockCommand>('/inventory/adjustments')
export const useTransferStock = () => useStockMutation<TransferStockCommand>('/transfers')

export const MOVEMENT_LABELS: Record<InventoryMovementType, string> = {
  StockIn: 'Stock in',
  StockOut: 'Stock out',
  AdjustmentIncrease: 'Adjustment +',
  AdjustmentDecrease: 'Adjustment −',
  TransferOut: 'Transfer out',
  TransferIn: 'Transfer in',
  PurchaseReceipt: 'Purchase receipt',
  SalesFulfilment: 'Sale',
}

export const ADJUSTMENT_REASONS = [
  { value: 'CountCorrection', label: 'Count correction' },
  { value: 'Damaged', label: 'Damaged' },
  { value: 'Lost', label: 'Lost or stolen' },
  { value: 'Found', label: 'Found' },
  { value: 'Expired', label: 'Expired' },
  { value: 'Other', label: 'Other' },
] as const
