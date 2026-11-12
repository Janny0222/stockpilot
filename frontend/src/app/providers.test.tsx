import { describe, expect, it } from 'vitest'
import { useAuthStore } from '@/stores/auth-store'
import { makeUser } from '@/test/server'
import { createQueryClient } from './providers'

/**
 * Security review finding: an expired or revoked session used to leave the previous user's cached lists in memory,
 * so the next person signing in on the same browser could briefly see another company's data.
 */
describe('query cache and the signed-in user', () => {
  it("is emptied whenever the signed-in person changes, and kept when it's the same person", () => {
    const client = createQueryClient()
    useAuthStore.getState().setUser(makeUser({ id: 'user-a' }))
    client.setQueryData(['products', 'list'], ['tenant A data'])

    // Same person, profile reloaded (e.g. after renaming the company): cache stays.
    useAuthStore.getState().setUser(makeUser({ id: 'user-a', firstName: 'Renamed' }))
    expect(client.getQueryData(['products', 'list'])).toEqual(['tenant A data'])

    // Session ends without a deliberate sign-out (expired, revoked elsewhere): cache goes.
    useAuthStore.getState().clear('expired')
    expect(client.getQueryData(['products', 'list'])).toBeUndefined()

    // A different person signs in on the same tab: nothing of A's survives.
    useAuthStore.getState().setUser(makeUser({ id: 'user-a' }))
    client.setQueryData(['products', 'list'], ['tenant A data'])
    useAuthStore.getState().setUser(makeUser({ id: 'user-b' }))
    expect(client.getQueryData(['products', 'list'])).toBeUndefined()
  })
})
