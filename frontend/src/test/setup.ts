import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterAll, afterEach, beforeAll } from 'vitest'
import { useAuthStore } from '@/stores/auth-store'
import { server } from './server'

// MSW intercepts real HTTP calls made by axios, so tests exercise the actual API client code
// (interceptors, refresh logic, error parsing) instead of mocking it away.
// A request with no matching handler fails the test, so a typo'd URL can't pass silently.
beforeAll(() => server.listen({ onUnhandledFrame: 'error' }))

afterEach(() => {
  server.resetHandlers()
  cleanup()
  useAuthStore.setState({ accessToken: null, user: null, status: 'unauthenticated', endedBy: null })
})

afterAll(() => server.close())
