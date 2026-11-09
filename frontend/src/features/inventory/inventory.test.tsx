import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import type { StockLevelDto } from '@/lib/api/types'
import { renderRoute } from '@/test/render'
import { apiUrl, problem, server } from '@/test/server'
import { AdjustDialog } from './components/adjust-dialog'

const row: StockLevelDto = {
  warehouseId: 'w1', warehouseCode: 'MNL', warehouseName: 'Manila', productId: 'p1', productName: 'LED Bulb 9W',
  variantId: 'v1', sku: 'LED-9W', variantName: null, unitOfMeasure: 'pcs', onHand: 10, reserved: 0, available: 10,
  reorderLevel: 5, status: 'InStock', averageUnitCost: 60, stockValue: 600,
}

describe('AdjustDialog', () => {
  it('shows the difference between the count and the system quantity', async () => {
    renderRoute(<AdjustDialog row={row} open onOpenChange={() => undefined} />)

    const counted = await screen.findByLabelText('Counted quantity')
    await userEvent.setup().clear(counted)
    await userEvent.setup().type(counted, '7')

    expect(screen.getByText('−3')).toBeInTheDocument()
  })

  it('sends an Idempotency-Key and REUSES it when the user retries after a failure', async () => {
    const keys: (string | null)[] = []
    let attempt = 0
    server.use(
      http.post(apiUrl('/inventory/adjustments'), async ({ request }) => {
        keys.push(request.headers.get('Idempotency-Key'))
        attempt++
        return attempt === 1
          ? problem(503, 'UNAVAILABLE', 'Temporary problem')
          : HttpResponse.json({ correlationId: 'c', documentNumber: null, movements: [] }, { status: 201 })
      }),
    )
    const user = userEvent.setup()
    renderRoute(<AdjustDialog row={row} open onOpenChange={() => undefined} />)

    const counted = await screen.findByLabelText('Counted quantity')
    await user.clear(counted)
    await user.type(counted, '8')
    await user.click(screen.getByRole('button', { name: 'Save adjustment' }))
    expect(await screen.findByText('Temporary problem')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Save adjustment' }))

    await waitFor(() => expect(keys).toHaveLength(2))
    expect(keys[0]).toMatch(/^[0-9a-f-]{36}$/)
    expect(keys[1]).toBe(keys[0]) // same user action → same key → the server can never apply it twice
  })
})
