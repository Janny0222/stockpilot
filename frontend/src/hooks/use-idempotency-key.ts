import { useCallback, useState } from 'react'

/**
 * One key per user ACTION, not per HTTP request. If the user double-clicks "Save", or the network drops
 * and they press it again, the same key is sent and the API returns the first result instead of moving
 * stock twice. Call `renew()` after a success so the NEXT action gets a fresh key.
 */
export function useIdempotencyKey() {
  const [key, setKey] = useState(() => crypto.randomUUID())
  const renew = useCallback(() => setKey(crypto.randomUUID()), [])
  return { key, renew }
}
