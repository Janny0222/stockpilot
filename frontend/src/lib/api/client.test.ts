import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { apiUrl, server } from '@/test/server'
import { useAuthStore } from '@/stores/auth-store'
import { api } from './client'
import { toApiError, type ApiError } from './errors'

/** Awaits a request that MUST fail and returns the normalized error. */
async function failure(request: Promise<unknown>): Promise<ApiError> {
  try {
    await request
  } catch (e) {
    return toApiError(e)
  }
  throw new Error('Expected the request to fail')
}

describe('api client', () => {
  it('sends the in-memory access token as a Bearer header', async () => {
    useAuthStore.setState({ accessToken: 'token-abc' })
    let seen: string | null = null
    server.use(
      http.get(apiUrl('/warehouses'), ({ request }) => {
        seen = request.headers.get('authorization')
        return HttpResponse.json({ items: [] })
      }),
    )

    await api.get('/warehouses')

    expect(seen).toBe('Bearer token-abc')
  })

  it('on 401 refreshes ONCE for concurrent requests, then replays them with the new token', async () => {
    useAuthStore.setState({ accessToken: 'expired' })
    let refreshCalls = 0
    server.use(
      http.post(apiUrl('/auth/refresh'), async () => {
        refreshCalls++
        await new Promise((r) => setTimeout(r, 20)) // keep the refresh "in flight" while others queue
        return HttpResponse.json({ accessToken: 'fresh', expiresAt: '2030-01-01T00:00:00Z' })
      }),
      http.get(apiUrl('/warehouses'), ({ request }) =>
        request.headers.get('authorization') === 'Bearer fresh'
          ? HttpResponse.json({ ok: true })
          : new HttpResponse(null, { status: 401 }),
      ),
    )

    const results = await Promise.all([api.get('/warehouses'), api.get('/warehouses'), api.get('/warehouses')])

    expect(results.every((r) => r.data.ok)).toBe(true)
    expect(refreshCalls).toBe(1)
    expect(useAuthStore.getState().accessToken).toBe('fresh')
  })

  it('ends the session when the refresh token is rejected', async () => {
    useAuthStore.setState({ accessToken: 'expired', status: 'authenticated' })
    server.use(
      http.post(apiUrl('/auth/refresh'), () => new HttpResponse(null, { status: 401 })),
      http.get(apiUrl('/warehouses'), () => new HttpResponse(null, { status: 401 })),
    )

    await expect(api.get('/warehouses')).rejects.toMatchObject({ response: { status: 401 } })

    const state = useAuthStore.getState()
    expect(state.status).toBe('unauthenticated')
    expect(state.accessToken).toBeNull()
    expect(state.endedBy).toBe('expired')
  })

  it('does not try to refresh when the login itself returns 401 (wrong password)', async () => {
    let refreshCalls = 0
    server.use(
      http.post(apiUrl('/auth/refresh'), () => {
        refreshCalls++
        return new HttpResponse(null, { status: 401 })
      }),
      http.post(apiUrl('/auth/login'), () => new HttpResponse(null, { status: 401 })),
    )

    await expect(api.post('/auth/login', {})).rejects.toBeDefined()
    expect(refreshCalls).toBe(0)
  })

  it('normalizes ProblemDetails into ApiError with code, field errors and trace id', async () => {
    server.use(
      http.post(apiUrl('/auth/register'), () =>
        HttpResponse.json(
          { status: 400, code: 'VALIDATION_FAILED', detail: 'One or more fields are invalid.', errors: { email: ['Email is not valid.'] }, traceId: 'abc123' },
          { status: 400 },
        ),
      ),
    )

    const error = await failure(api.post('/auth/register', {}))

    expect(error).toMatchObject({ status: 400, code: 'VALIDATION_FAILED', traceId: 'abc123' })
    expect(error.fieldErrors.email).toEqual(['Email is not valid.'])
  })

  it('reports an unreachable server as a network error, not a crash', async () => {
    server.use(http.get(apiUrl('/warehouses'), () => HttpResponse.error()))

    const error = await failure(api.get('/warehouses'))

    expect(error.code).toBe('NETWORK_ERROR')
  })
})
