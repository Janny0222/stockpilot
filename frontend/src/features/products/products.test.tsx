import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { renderRoute } from '@/test/render'
import { apiUrl, makeUser, problem, server } from '@/test/server'
import { useAuthStore } from '@/stores/auth-store'
import { NewProductPage } from './pages/new-product-page'
import { ProductsPage } from './pages/products-page'
import { createProductSchema, emptyVariant } from './schemas'

const owner = makeUser({ permissions: ['products.view', 'products.manage', 'inventory.view_cost'] })

describe('product form rules', () => {
  const base = { name: 'Hammer', unitOfMeasure: 'pcs' as const, description: '', categoryId: undefined }
  const variant = (sku: string, price = 10) => ({ ...emptyVariant(), sku, costPrice: 5, sellingPrice: price })

  it('flags a SKU repeated within the same product, ignoring case', () => {
    const result = createProductSchema.safeParse({ ...base, variants: [variant('HAM-1'), variant('ham-1')] })
    expect(result.success).toBe(false)
    expect(result.error?.issues.some((i) => i.path.join('.') === 'variants.1.sku')).toBe(true)
  })

  it('asks for a price when the field is left empty (NaN from a number input)', () => {
    const result = createProductSchema.safeParse({ ...base, variants: [{ ...variant('HAM-1'), sellingPrice: Number.NaN }] })
    expect(result.error?.issues[0]?.message).toBe('Enter the selling price.')
  })

  it('rejects negative prices and spaces in SKUs', () => {
    expect(createProductSchema.safeParse({ ...base, variants: [variant('HAM-1', -1)] }).success).toBe(false)
    expect(createProductSchema.safeParse({ ...base, variants: [variant('HAM 1')] }).success).toBe(false)
  })
})

describe('ProductsPage', () => {
  it('lists products with formatted prices and sends the search to the API', async () => {
    useAuthStore.setState({ status: 'authenticated', user: owner })
    const searches: (string | null)[] = []
    server.use(
      http.get(apiUrl('/categories'), () => HttpResponse.json([])),
      http.get(apiUrl('/products'), ({ request }) => {
        searches.push(new URL(request.url).searchParams.get('search'))
        return HttpResponse.json({
          items: [
            {
              id: 'p1', name: 'THHN Wire 3.5mm', categoryId: null, categoryName: 'Wires', unitOfMeasure: 'roll', status: 'Active',
              variantCount: 3, sku: 'THHN-35-RED', minPrice: 2950, maxPrice: 3050, primaryImageId: null, createdAt: '2026-01-01T00:00:00Z',
            },
          ],
          page: 1, pageSize: 15, totalCount: 1,
        })
      }),
    )
    renderRoute(<ProductsPage />, { path: '/products' })

    expect(await screen.findByText('THHN Wire 3.5mm')).toBeInTheDocument()
    expect(screen.getByText('THHN-35-RED +2 more')).toBeInTheDocument()
    expect(screen.getByText(/₱2,950\.00 – ₱3,050\.00/)).toBeInTheDocument()

    await userEvent.setup().type(screen.getByLabelText('Search products'), 'thhn')
    await waitFor(() => expect(searches).toContain('thhn'))
  })

  it('hides "Add product" from roles that cannot manage products', async () => {
    useAuthStore.setState({ status: 'authenticated', user: makeUser({ permissions: ['products.view'] }) })
    server.use(
      http.get(apiUrl('/categories'), () => HttpResponse.json([])),
      http.get(apiUrl('/products'), () => HttpResponse.json({ items: [], page: 1, pageSize: 15, totalCount: 0 })),
    )
    renderRoute(<ProductsPage />, { path: '/products' })

    expect(await screen.findByText('No products yet')).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: /add product/i })).not.toBeInTheDocument()
  })
})

describe('NewProductPage', () => {
  async function fill(sku: string) {
    const user = userEvent.setup()
    await user.type(screen.getByLabelText('Product name'), 'LED Bulb 9W')
    await user.type(screen.getByLabelText('SKU'), sku)
    await user.type(screen.getByLabelText('Cost price'), '60')
    await user.type(screen.getByLabelText('Selling price'), '95')
    await user.click(screen.getByRole('button', { name: 'Save product' }))
  }

  it('shows "SKU already used" on the SKU field of the clashing variant', async () => {
    useAuthStore.setState({ status: 'authenticated', user: owner })
    server.use(
      http.get(apiUrl('/categories'), () => HttpResponse.json([])),
      http.post(apiUrl('/products'), () => problem(409, 'SKU_TAKEN', "SKU 'LED-9W' is already used by another product.")),
    )
    renderRoute(<NewProductPage />, { path: '/products/new' })

    await fill('led-9w')

    expect(await screen.findByText("SKU 'LED-9W' is already used by another product.")).toBeInTheDocument()
    expect(screen.getByLabelText('SKU')).toHaveAttribute('aria-invalid', 'true')
  })

  it('creates the product and opens its page', async () => {
    useAuthStore.setState({ status: 'authenticated', user: owner })
    let body: { variants?: { sku: string; barcode: unknown }[] } = {}
    server.use(
      http.get(apiUrl('/categories'), () => HttpResponse.json([])),
      http.post(apiUrl('/products'), async ({ request }) => {
        body = (await request.json()) as typeof body
        return HttpResponse.json({ id: 'new-id', name: 'LED Bulb 9W' }, { status: 201 })
      }),
    )
    renderRoute(<NewProductPage />, { path: '/products/new' })

    await fill('LED-9W')

    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent('/products/new-id'))
    expect(body.variants?.[0]).toMatchObject({ sku: 'LED-9W', barcode: null }) // empty input sent as null, not ""
  })
})
