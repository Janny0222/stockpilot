import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { beforeEach, describe, expect, it } from 'vitest'
import type { PurchaseOrderDetailDto } from '@/lib/api/types'
import { useAuthStore } from '@/stores/auth-store'
import { renderRoute } from '@/test/render'
import { apiUrl, makeUser, problem, server } from '@/test/server'
import { PurchaseOrderDetailPage } from './pages/purchase-order-detail-page'
import { PurchaseOrderFormPage } from './pages/purchase-order-form-page'

const po: PurchaseOrderDetailDto = {
  id: 'po1',
  number: 'PO-000007',
  status: 'Approved',
  supplierId: 's1',
  supplierName: 'Luzon Electrical Supply',
  warehouseId: 'w1',
  warehouseName: 'Manila',
  orderDate: '2026-11-01',
  expectedDate: '2026-11-12',
  notes: null,
  total: 1200,
  version: 3,
  canViewCost: true,
  createdBy: 'Owner',
  createdAt: '2026-11-01T00:00:00Z',
  submittedAt: '2026-11-01T01:00:00Z',
  approvedAt: '2026-11-01T02:00:00Z',
  approvedBy: 'Owner',
  cancelledAt: null,
  cancelReason: null,
  items: [
    {
      id: 'i1',
      variantId: 'v1',
      productId: 'p1',
      productName: 'LED Bulb 9W',
      sku: 'LED-9W',
      variantName: null,
      unitOfMeasure: 'pcs',
      quantityOrdered: 10,
      quantityReceived: 4,
      quantityRemaining: 6,
      unitCost: 60,
      lineTotal: 600,
    },
    {
      id: 'i2',
      variantId: 'v2',
      productId: 'p2',
      productName: 'Extension Cord',
      sku: 'EXT-3M',
      variantName: null,
      unitOfMeasure: 'pcs',
      quantityOrdered: 5,
      quantityReceived: 5,
      quantityRemaining: 0,
      unitCost: 120,
      lineTotal: 600,
    },
  ],
  receipts: [],
}

const renderDetail = () => renderRoute(<PurchaseOrderDetailPage />, { path: '/purchases/:id', url: '/purchases/po1' })

describe('Purchase order: receiving', () => {
  beforeEach(() => {
    useAuthStore.setState({ status: 'authenticated', user: makeUser({ permissions: ['purchases.view', 'purchases.receive'] }) })
    server.use(http.get(apiUrl('/purchase-orders/po1'), () => HttpResponse.json(po)))
  })

  it('pre-fills only the outstanding lines and blocks receiving more than is left', async () => {
    let posted = 0
    server.use(
      http.post(apiUrl('/purchase-orders/po1/receipts'), () => {
        posted++
        return HttpResponse.json({})
      }),
    )
    const user = userEvent.setup()
    renderDetail()

    await user.click(await screen.findByRole('button', { name: 'Receive delivery' }))
    const qty = await screen.findByRole('spinbutton', { name: /LED Bulb 9W/ })
    expect(qty).toHaveValue(6)
    expect(screen.queryByRole('spinbutton', { name: /Extension Cord/ })).not.toBeInTheDocument() // fully received

    await user.clear(qty)
    await user.type(qty, '7')
    await user.click(screen.getByRole('button', { name: 'Record receipt' }))

    expect(await screen.findByText(/LED-9W: enter between 0 and 6/)).toBeInTheDocument()
    expect(posted).toBe(0)
  })

  it('REUSES the Idempotency-Key when the user retries after a failure', async () => {
    const keys: (string | null)[] = []
    server.use(
      http.post(apiUrl('/purchase-orders/po1/receipts'), ({ request }) => {
        keys.push(request.headers.get('Idempotency-Key'))
        return keys.length === 1
          ? problem(503, 'UNAVAILABLE', 'Temporary problem')
          : HttpResponse.json({ receiptId: 'r1', receiptNumber: 'GRN-000002', status: 'Received' }, { status: 201 })
      }),
    )
    const user = userEvent.setup()
    renderDetail()

    await user.click(await screen.findByRole('button', { name: 'Receive delivery' }))
    await user.click(await screen.findByRole('button', { name: 'Record receipt' }))
    expect(await screen.findByText('Temporary problem')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Record receipt' }))

    await waitFor(() => expect(keys).toHaveLength(2))
    expect(keys[0]).toMatch(/^[0-9a-f-]{36}$/)
    expect(keys[1]).toBe(keys[0]) // a retried delivery can never be booked into stock twice
  })

  it('hides the receive action from users without purchases.receive', async () => {
    useAuthStore.setState({ status: 'authenticated', user: makeUser({ permissions: ['purchases.view'] }) })
    renderDetail()

    expect(await screen.findByText('LED Bulb 9W')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Receive delivery' })).not.toBeInTheDocument()
  })
})

describe('Purchase order form', () => {
  it('requires a supplier, a warehouse and at least one item before saving', async () => {
    let posted = 0
    useAuthStore.setState({ status: 'authenticated', user: makeUser({ permissions: ['purchases.view', 'purchases.manage'] }) })
    server.use(
      http.get(apiUrl('/suppliers'), () => HttpResponse.json({ items: [], page: 1, pageSize: 100, totalCount: 0 })),
      http.get(apiUrl('/warehouses'), () => HttpResponse.json({ items: [], page: 1, pageSize: 100, totalCount: 0 })),
      http.post(apiUrl('/purchase-orders'), () => {
        posted++
        return HttpResponse.json({ id: 'x' }, { status: 201 })
      }),
    )
    const user = userEvent.setup()
    renderRoute(<PurchaseOrderFormPage />, { path: '/purchases/new' })

    await user.click(await screen.findByRole('button', { name: 'Create draft' }))

    expect(await screen.findByText('Choose a supplier.')).toBeInTheDocument()
    expect(screen.getByText('Choose where it will be delivered.')).toBeInTheDocument()
    expect(screen.getByText('Add at least one item.')).toBeInTheDocument()
    expect(posted).toBe(0)
  })
})
