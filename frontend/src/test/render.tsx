import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import type { ReactElement } from 'react'
import { createMemoryRouter, RouterProvider, useLocation, type RouteObject } from 'react-router'

/** Renders a page inside a real router + query client, as it runs in the app. */
export function renderRoute(element: ReactElement, { path = '/', url = path, extraRoutes = [] }: { path?: string; url?: string; extraRoutes?: RouteObject[] } = {}) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  const router = createMemoryRouter([{ path, element }, ...extraRoutes, { path: '*', element: <LocationProbe /> }], {
    initialEntries: [url],
  })

  const view = render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  )
  return { ...view, router }
}

/** Fallback route that prints where navigation ended up, so tests can assert redirects. */
export function LocationProbe() {
  const location = useLocation()
  return <p data-testid="location">{location.pathname + location.search}</p>
}
