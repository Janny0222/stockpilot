import { fireEvent, render, screen } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import type { DashboardDto, ReportTableDto } from '@/lib/api/types'
import { useAuthStore } from '@/stores/auth-store'
import { renderRoute } from '@/test/render'
import { apiUrl, makeUser, server } from '@/test/server'
import { ReportPage } from '@/features/reports/reports-pages'
import { ColumnChart } from './charts'
import { DashboardPage } from './dashboard-page'

const as = (...permissions: string[]) => useAuthStore.setState({ status: 'authenticated', user: makeUser({ permissions }) })
const empty = { items: [], page: 1, pageSize: 5, totalCount: 0 }

const dashboard = (over: Partial<DashboardDto> = {}): DashboardDto => ({
  days: 30,
  from: '2026-10-12',
  to: '2026-11-10',
  canViewCost: true,
  stock: { value: 1400, units: 14, reservedUnits: 2, itemsInStock: 1, lowStock: 1, outOfStock: 0 },
  sales: { revenue: 900, previousRevenue: 600, ordersShipped: 1, openOrders: 1, grossProfit: 300, previousGrossProfit: 200, marginPercent: 33.3 },
  purchasing: { openOrders: 2, awaitingApproval: 1, overdue: 0, incomingUnits: 40, incomingValue: 4000 },
  salesTrend: [
    { date: '2026-11-09', revenue: 0, grossProfit: 0 },
    { date: '2026-11-10', revenue: 900, grossProfit: 300 },
  ],
  topProducts: [{ productId: 'p1', productName: 'LED Bulb 9W', quantity: 6, revenue: 900 }],
  stockByWarehouse: [{ warehouseId: 'w1', code: 'MNL', name: 'Manila', units: 14, value: 1400, lowStock: 1, outOfStock: 0 }],
  ...over,
})

function serve(d: DashboardDto) {
  server.use(
    http.get(apiUrl('/dashboard'), () => HttpResponse.json(d)),
    http.get(apiUrl('/warehouses'), () => HttpResponse.json({ ...empty, pageSize: 100 })),
    http.get(apiUrl('/inventory/stock'), () => HttpResponse.json(empty)),
    http.get(apiUrl('/inventory/movements'), () => HttpResponse.json(empty)),
  )
}

describe('DashboardPage', () => {
  it('shows the real figures with the change vs the previous period', async () => {
    as('dashboard.view', 'inventory.view', 'inventory.view_cost', 'sales.view', 'purchases.view', 'warehouses.view')
    serve(dashboard())
    renderRoute(<DashboardPage />)

    expect((await screen.findAllByText('Revenue')).length).toBeGreaterThan(0)
    expect(screen.getAllByText('+50.0% vs previous 30 days')).toHaveLength(2) // revenue 900 vs 600, profit 300 vs 200
    expect(screen.getByText('33.3% margin')).toBeInTheDocument()
    expect(screen.getByText('Stock value')).toBeInTheDocument()
    expect(screen.getByText('Best sellers')).toBeInTheDocument()
    expect(screen.getByText('1 awaiting approval')).toBeInTheDocument()
  })

  it('shows Sales Staff their sales, but no stock value, profit or purchasing', async () => {
    as('dashboard.view', 'sales.view', 'inventory.view')
    serve(
      dashboard({
        canViewCost: false,
        purchasing: null,
        stock: { value: null, units: 14, reservedUnits: 2, itemsInStock: 1, lowStock: 1, outOfStock: 0 },
        sales: { ...dashboard().sales!, grossProfit: null, previousGrossProfit: null, marginPercent: null },
      }),
    )
    renderRoute(<DashboardPage />)

    expect((await screen.findAllByText('Revenue')).length).toBeGreaterThan(0)
    expect(screen.getByText('Units on hand')).toBeInTheDocument()
    expect(screen.queryByText('Stock value')).not.toBeInTheDocument()
    expect(screen.queryByText('Gross profit')).not.toBeInTheDocument()
    expect(screen.queryByText('Open purchase orders')).not.toBeInTheDocument()
  })

  it('never shows an infinite percentage when the previous period had no sales', async () => {
    as('dashboard.view', 'sales.view')
    serve(
      dashboard({
        stock: null,
        purchasing: null,
        stockByWarehouse: null,
        canViewCost: false,
        sales: { ...dashboard().sales!, previousRevenue: 0, grossProfit: null, previousGrossProfit: null, marginPercent: null },
      }),
    )
    renderRoute(<DashboardPage />)

    expect(await screen.findByText('vs nothing in the previous 30 days')).toBeInTheDocument()
    expect(screen.queryByText(/Infinity|NaN/)).not.toBeInTheDocument()
  })
})

describe('ColumnChart', () => {
  it('reads each period aloud with the arrow keys and keeps a data table for screen readers', () => {
    // jsdom has no layout: give the chart a width so the SVG is actually drawn.
    const rect = vi
      .spyOn(HTMLElement.prototype, 'getBoundingClientRect')
      .mockReturnValue({ width: 600, height: 240, top: 0, left: 0, right: 600, bottom: 240, x: 0, y: 0, toJSON: () => ({}) } as DOMRect)
    render(
      <ColumnChart
        categories={[
          { key: 'a', axisLabel: 'Nov 9', title: 'Mon, Nov 9' },
          { key: 'b', axisLabel: 'Nov 10', title: 'Tue, Nov 10' },
        ]}
        series={[{ key: 'r', label: 'Revenue', color: '#5a3cf0', values: [0, 900] }]}
        format={(v) => `₱${v}`}
        formatAxis={String}
        label="Sales per day"
      />,
    )

    // The data table is always there (jsdom has no layout, so the SVG itself isn't drawn here).
    expect(screen.getByRole('table', { name: 'Sales per day' })).toBeInTheDocument()
    expect(screen.getByRole('row', { name: /Tue, Nov 10 ₱900/ })).toBeInTheDocument()

    const svg = screen.getByRole('img', { name: /Sales per day/ })
    fireEvent.keyDown(svg, { key: 'ArrowLeft' }) // from nothing selected, Left starts at the latest period
    expect(screen.getByRole('status')).toHaveTextContent('Tue, Nov 10')
    expect(screen.getByRole('status')).toHaveTextContent('₱900')
    fireEvent.keyDown(svg, { key: 'ArrowLeft' })
    expect(screen.getByRole('status')).toHaveTextContent('Mon, Nov 9')
    rect.mockRestore()
  })
})

describe('ReportPage', () => {
  const report: ReportTableDto = {
    key: 'sales',
    title: 'Sales',
    description: 'Goods shipped to customers in the period.',
    from: '2026-10-12',
    to: '2026-11-10',
    columns: [
      { key: 'name', label: 'Customer', kind: 'Text' },
      { key: 'revenue', label: 'Revenue', kind: 'Money' },
      { key: 'margin', label: 'Margin', kind: 'Percent' },
    ],
    rows: [['BuildRight Construction Co.', 12080, 29]],
    totals: ['Total', 12080, 29],
    truncated: false,
  }

  it('renders any report from its columns, with totals, and hides export without reports.export', async () => {
    as('reports.view')
    let groupBy: string | null = null
    server.use(
      http.get(apiUrl('/reports/sales'), ({ request }) => {
        groupBy = new URL(request.url).searchParams.get('groupBy')
        return HttpResponse.json(report)
      }),
      http.get(apiUrl('/warehouses'), () => HttpResponse.json({ ...empty, pageSize: 100 })),
    )
    renderRoute(<ReportPage />, { path: '/reports/:key', url: '/reports/sales?groupBy=customer' })

    expect(await screen.findByText('BuildRight Construction Co.')).toBeInTheDocument()
    expect(screen.getAllByText('29.0%')).toHaveLength(2) // row + totals
    expect(screen.getByText('Total')).toBeInTheDocument()
    expect(groupBy).toBe('customer')
    expect(screen.queryByRole('button', { name: /Export CSV/ })).not.toBeInTheDocument()
  })
})
