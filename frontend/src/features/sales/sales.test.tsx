import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { beforeEach, describe, expect, it } from 'vitest'
import type { SalesOrderDetailDto } from '@/lib/api/types'
import { useAuthStore } from '@/stores/auth-store'
import { renderRoute } from '@/test/render'
import { apiUrl, makeUser, problem, server } from '@/test/server'
import { SalesOrderDetailPage } from './pages/sales-order-detail-page'
import { SalesOrderFormPage } from './pages/sales-order-form-page'

const item = (over: Partial<SalesOrderDetailDto['items'][number]>): SalesOrderDetailDto['items'][number] => ({
  id: 'i1',
  variantId: 'v1',
  productId: 'p1',
  productName: 'LED Bulb 9W',
  sku: 'LED-9W',
  variantName: null,
  unitOfMeasure: 'pcs',
  quantityOrdered: 10,
  quantityShipped: 0,
  quantityRemaining: 10,
  unitPrice: 95,
  lineTotal: 950,
  available: 50,
  ...over,
})

const order = (over: Partial<SalesOrderDetailDto> = {}): SalesOrderDetailDto => ({
  id: 'so1',
  number: 'SO-000014',
  status: 'Confirmed',
  customerId: 'c1',
  customerName: 'BuildRight Construction Co.',
  warehouseId: 'w1',
  warehouseName: 'Manila',
  orderDate: '2026-11-09',
  notes: null,
  total: 950,
  version: 2,
  createdBy: 'Ana Garcia',
  createdAt: '2026-11-09T00:00:00Z',
  confirmedAt: '2026-11-09T01:00:00Z',
  confirmedBy: 'Ana Garcia',
  cancelledAt: null,
  cancelReason: null,
  profit: null,
  items: [item({ quantityShipped: 4, quantityRemaining: 6 })],
  shipments: [],
  ...over,
})

const renderDetail = () => renderRoute(<SalesOrderDetailPage />, { path: '/sales/:id', url: '/sales/so1' })
const as = (...permissions: string[]) => useAuthStore.setState({ status: 'authenticated', user: makeUser({ permissions }) })

describe('Sales order: shipping', () => {
  beforeEach(() => {
    as('sales.view', 'sales.fulfil')
    server.use(http.get(apiUrl('/sales-orders/so1'), () => HttpResponse.json(order())))
  })

  it('pre-fills what is left to ship and blocks shipping more', async () => {
    let posted = 0
    server.use(
      http.post(apiUrl('/sales-orders/so1/shipments'), () => {
        posted++
        return HttpResponse.json({})
      }),
    )
    const user = userEvent.setup()
    renderDetail()

    await user.click(await screen.findByRole('button', { name: 'Ship' }))
    const qty = await screen.findByRole('spinbutton', { name: /LED Bulb 9W/ })
    expect(qty).toHaveValue(6)

    await user.clear(qty)
    await user.type(qty, '7')
    await user.click(screen.getByRole('button', { name: 'Record shipment' }))

    expect(await screen.findByText(/LED-9W: enter between 0 and 6/)).toBeInTheDocument()
    expect(posted).toBe(0)
  })

  it('REUSES the Idempotency-Key when the user retries after a failure', async () => {
    const keys: (string | null)[] = []
    server.use(
      http.post(apiUrl('/sales-orders/so1/shipments'), ({ request }) => {
        keys.push(request.headers.get('Idempotency-Key'))
        return keys.length === 1
          ? problem(503, 'UNAVAILABLE', 'Temporary problem')
          : HttpResponse.json({ shipmentId: 's1', shipmentNumber: 'DR-000011', status: 'Fulfilled' }, { status: 201 })
      }),
    )
    const user = userEvent.setup()
    renderDetail()

    await user.click(await screen.findByRole('button', { name: 'Ship' }))
    await user.click(await screen.findByRole('button', { name: 'Record shipment' }))
    expect(await screen.findByText('Temporary problem')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Record shipment' }))

    await waitFor(() => expect(keys).toHaveLength(2))
    expect(keys[0]).toMatch(/^[0-9a-f-]{36}$/)
    expect(keys[1]).toBe(keys[0]) // a retried shipment can never take the stock out twice
  })

  it('offers "Cancel the rest" only to users with sales.cancel', async () => {
    renderDetail()
    expect(await screen.findByText('LED Bulb 9W')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Cancel/ })).not.toBeInTheDocument()
  })
})

describe('Sales order: drafts', () => {
  it('warns before confirming when a line has more than is available', async () => {
    as('sales.view', 'sales.create')
    server.use(
      http.get(apiUrl('/sales-orders/so1'), () =>
        HttpResponse.json(order({ status: 'Draft', confirmedAt: null, confirmedBy: null, items: [item({ quantityOrdered: 10, available: 3 })] })),
      ),
    )
    renderDetail()

    expect(await screen.findByText(/Not enough available in Manila for LED-9W \(3 available\)/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Confirm order' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Ship' })).not.toBeInTheDocument()
  })

  it('requires a customer, a warehouse and at least one item before saving', async () => {
    let posted = 0
    as('sales.view', 'sales.create')
    server.use(
      http.get(apiUrl('/customers'), () => HttpResponse.json({ items: [], page: 1, pageSize: 100, totalCount: 0 })),
      http.get(apiUrl('/warehouses'), () => HttpResponse.json({ items: [], page: 1, pageSize: 100, totalCount: 0 })),
      http.post(apiUrl('/sales-orders'), () => {
        posted++
        return HttpResponse.json({ id: 'x' }, { status: 201 })
      }),
    )
    const user = userEvent.setup()
    renderRoute(<SalesOrderFormPage />, { path: '/sales/new' })

    await user.click(await screen.findByRole('button', { name: 'Create draft' }))

    expect(await screen.findByText('Choose a customer.')).toBeInTheDocument()
    expect(screen.getByText('Choose where it ships from.')).toBeInTheDocument()
    expect(screen.getByText('Add at least one item.')).toBeInTheDocument()
    expect(posted).toBe(0)
  })
})
