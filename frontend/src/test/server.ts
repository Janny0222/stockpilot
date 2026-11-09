import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import type { MeResponse } from '@/lib/api/types'

export const server = setupServer()

/** Matches any origin, so tests don't depend on jsdom's base URL. */
export const apiUrl = (path: string) => `*/api/v1${path}`

export function problem(status: number, code: string, detail: string, errors?: Record<string, string[]>) {
  return HttpResponse.json({ status, code, detail, title: 'Error', errors, traceId: 'test-trace' }, { status })
}

export function makeUser(overrides: Partial<MeResponse> = {}): MeResponse {
  return {
    id: '00000000-0000-7000-8000-000000000001',
    email: 'owner@acme.test',
    firstName: 'Maria',
    lastName: 'Santos',
    isPlatformAdmin: false,
    role: { id: 'r1', name: 'Owner' },
    permissions: ['dashboard.view', 'warehouses.view'],
    tenant: { id: 't1', name: 'Acme Trading', slug: 'acme', currency: 'PHP', timeZone: 'Asia/Manila' },
    warehouseScope: { restricted: false, warehouseIds: [] },
    ...overrides,
  }
}

export const handlers = {
  loginOk: (token = 'access-1') => http.post(apiUrl('/auth/login'), () => HttpResponse.json({ accessToken: token, expiresAt: '2030-01-01T00:00:00Z' })),
  meOk: (user = makeUser()) => http.get(apiUrl('/auth/me'), () => HttpResponse.json(user)),
}
