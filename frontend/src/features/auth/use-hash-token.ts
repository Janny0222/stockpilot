import { useEffect, useState } from 'react'

/**
 * Email links carry their token in the URL fragment: /reset-password#token=abc.
 * Fragments are never sent to servers, so the token can't leak into proxy logs or Referer headers.
 *
 * Read it during render (pure), then strip it from the address bar in an effect so it isn't left
 * in browser history. The strip must NOT happen in the useState initializer: React StrictMode calls
 * initializers twice in development, and the second call would find the fragment already gone.
 */
export function useHashToken() {
  const [token] = useState(() => new URLSearchParams(window.location.hash.slice(1)).get('token'))

  useEffect(() => {
    if (token && window.location.hash) {
      window.history.replaceState(null, '', window.location.pathname + window.location.search)
    }
  }, [token])

  return token
}
