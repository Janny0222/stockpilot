import '@testing-library/jest-dom/vitest'
import { cleanup, configure } from '@testing-library/react'
import { afterAll, afterEach, beforeAll } from 'vitest'
import { useAuthStore } from '@/stores/auth-store'
import { server } from './server'

// MSW intercepts real HTTP calls made by axios, so tests exercise the actual API client code
// (interceptors, refresh logic, error parsing) instead of mocking it away.
// A request with no matching handler fails the test, so a typo'd URL can't pass silently.
beforeAll(() => server.listen({ onUnhandledFrame: 'error' }))

// findBy*/waitFor default to 1s. With every test file running in parallel on a slow laptop (or a shared CI runner) a
// mocked request can take longer than that, which made tests flaky without anything being wrong. Waiting longer
// doesn't weaken any assertion: a broken page still fails, just after 3s instead of 1s.
configure({ asyncUtilTimeout: 3000 })

// jsdom lacks the pointer-capture and scrolling APIs Radix Select calls when it opens.
Element.prototype.hasPointerCapture ??= () => false
Element.prototype.releasePointerCapture ??= () => {}
Element.prototype.scrollIntoView ??= () => {}

afterEach(() => {
  server.resetHandlers()
  cleanup()
  useAuthStore.setState({ accessToken: null, user: null, status: 'unauthenticated', endedBy: null })
})

afterAll(() => server.close())
