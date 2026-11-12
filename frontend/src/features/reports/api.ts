import { useMutation, useQuery } from '@tanstack/react-query'
import { api } from '@/lib/api/client'
import type { InventoryMovementType, ReportTableDto } from '@/lib/api/types'

export type ReportKey = 'inventory-valuation' | 'low-stock' | 'sales' | 'purchases' | 'stock-movements'
export type ReportParams = { from?: string; to?: string; warehouseId?: string; groupBy?: string; type?: InventoryMovementType }

/** What each report offers. The server enforces the same rules; this only decides which filters to show. */
export const REPORTS: Record<ReportKey, { title: string; summary: string; dated: boolean; groupBy?: { value: string; label: string }[] }> = {
  'inventory-valuation': { title: 'Inventory valuation', summary: 'What you hold in each warehouse and what it is worth.', dated: false },
  'low-stock': { title: 'Low stock', summary: 'Items at or below their reorder level, with what is already on order.', dated: false },
  sales: {
    title: 'Sales',
    summary: 'Revenue, cost of goods and margin for goods shipped.',
    dated: true,
    groupBy: [
      { value: 'product', label: 'By product' },
      { value: 'customer', label: 'By customer' },
      { value: 'day', label: 'By day' },
    ],
  },
  purchases: {
    title: 'Purchases',
    summary: 'Goods received from suppliers and their value.',
    dated: true,
    groupBy: [
      { value: 'supplier', label: 'By supplier' },
      { value: 'product', label: 'By product' },
    ],
  },
  'stock-movements': { title: 'Stock movements', summary: 'Every change to stock, straight from the ledger.', dated: true },
}

export const isReportKey = (key: string | undefined): key is ReportKey => !!key && key in REPORTS

const clean = (p: ReportParams) => Object.fromEntries(Object.entries(p).filter(([, v]) => v !== undefined && v !== ''))

export function useReport(key: ReportKey, params: ReportParams) {
  return useQuery({
    queryKey: ['inventory', 'reports', key, params],
    queryFn: ({ signal }) => api.get<ReportTableDto>(`/reports/${key}`, { params: clean(params), signal }).then((r) => r.data),
    placeholderData: (previous) => (previous?.key === key ? previous : undefined),
  })
}

/** The CSV endpoint needs the bearer token, so fetch it as a blob through the API client and hand it to the browser. */
export function useDownloadReport() {
  return useMutation({
    mutationFn: async ({ key, params }: { key: ReportKey; params: ReportParams }) => {
      const response = await api.get<Blob>(`/reports/${key}/csv`, { params: clean(params), responseType: 'blob' })
      const disposition = String(response.headers['content-disposition'] ?? '')
      const name = /filename\*=UTF-8''([^;]+)/i.exec(disposition)?.[1] ?? /filename="?([^";]+)"?/i.exec(disposition)?.[1] ?? `${key}.csv`
      const url = URL.createObjectURL(response.data)
      const link = document.createElement('a')
      link.href = url
      link.download = safeDecode(name) ?? `${key}.csv` // a malformed %-sequence mustn't break the download
      document.body.appendChild(link)
      link.click()
      link.remove()
      setTimeout(() => URL.revokeObjectURL(url), 1000)
      return name
    },
  })
}

function safeDecode(value: string) {
  try {
    return decodeURIComponent(value)
  } catch {
    return null
  }
}
