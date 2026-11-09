import { useAuthStore } from '@/stores/auth-store'

const moneyFormatters = new Map<string, Intl.NumberFormat>()

/** ₱1,250.00. Uses the company's currency (one per company, assumption A6). */
export function formatMoney(value: number | null | undefined, currency = 'PHP') {
  if (value === null || value === undefined) return '—'
  let formatter = moneyFormatters.get(currency)
  if (!formatter) {
    formatter = new Intl.NumberFormat('en-PH', { style: 'currency', currency, minimumFractionDigits: 2 })
    moneyFormatters.set(currency, formatter)
  }
  return formatter.format(value)
}

/** "₱95.00" or "₱95.00 – ₱125.00" for products whose variants are priced differently. */
export function formatPriceRange(min: number | null | undefined, max: number | null | undefined, currency?: string) {
  if (min === null || min === undefined) return '—'
  return max !== null && max !== undefined && max !== min
    ? `${formatMoney(min, currency)} – ${formatMoney(max, currency)}`
    : formatMoney(min, currency)
}

const qty = new Intl.NumberFormat('en-PH', { maximumFractionDigits: 3 })
export const formatQuantity = (value: number) => qty.format(value)

export function useCurrency() {
  return useAuthStore((s) => s.user?.tenant?.currency ?? 'PHP')
}
