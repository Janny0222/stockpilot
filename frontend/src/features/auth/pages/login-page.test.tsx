import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { renderRoute } from '@/test/render'
import { apiUrl, handlers, makeUser, problem, server } from '@/test/server'
import { useAuthStore } from '@/stores/auth-store'
import { LoginPage } from './login-page'

const fillAndSubmit = async (email: string, password: string) => {
  const user = userEvent.setup()
  if (email) await user.type(screen.getByLabelText('Email'), email)
  if (password) await user.type(screen.getByLabelText('Password'), password)
  await user.click(screen.getByRole('button', { name: 'Sign in' }))
  return user
}

describe('LoginPage', () => {
  it('validates required fields before calling the API', async () => {
    renderRoute(<LoginPage />, { path: '/login' })

    await fillAndSubmit('', '')

    expect(await screen.findByText('Enter your email.')).toBeInTheDocument()
    expect(screen.getByText('Enter your password.')).toBeInTheDocument()
    expect(screen.getByLabelText('Email')).toHaveAttribute('aria-invalid', 'true')
  })

  it('rejects a malformed email', async () => {
    renderRoute(<LoginPage />, { path: '/login' })

    await fillAndSubmit('not-an-email', 'whatever')

    expect(await screen.findByText('Enter a valid email address.')).toBeInTheDocument()
  })

  it("shows the server's message for wrong credentials", async () => {
    server.use(http.post(apiUrl('/auth/login'), () => problem(401, 'INVALID_CREDENTIALS', 'Email or password is incorrect.')))
    renderRoute(<LoginPage />, { path: '/login' })

    await fillAndSubmit('maria@acme.test', 'Wrong-pass1')

    expect(await screen.findByRole('alert')).toHaveTextContent('Email or password is incorrect.')
    expect(useAuthStore.getState().accessToken).toBeNull()
  })

  it('signs in, loads the profile and returns to the requested page', async () => {
    server.use(handlers.loginOk('jwt-1'), handlers.meOk(makeUser({ firstName: 'Maria' })))
    renderRoute(<LoginPage />, { path: '/login', url: '/login?returnTo=%2Fwarehouses%3Fq%3Dmnl' })

    await fillAndSubmit('maria@acme.test', 'Correct-pass1')

    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent('/warehouses?q=mnl'))
    expect(useAuthStore.getState()).toMatchObject({ accessToken: 'jwt-1', status: 'authenticated' })
  })

  it('ignores an off-site returnTo (open-redirect protection)', async () => {
    server.use(handlers.loginOk(), handlers.meOk())
    renderRoute(<LoginPage />, { path: '/login', url: '/login?returnTo=%2F%2Fevil.example' })

    await fillAndSubmit('maria@acme.test', 'Correct-pass1')

    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent(/^\/$/))
  })

  it('offers to resend the verification email when the address is unverified', async () => {
    let resentTo: unknown
    server.use(
      http.post(apiUrl('/auth/login'), () => problem(403, 'EMAIL_NOT_VERIFIED', 'Please verify your email address before signing in.')),
      http.post(apiUrl('/auth/resend-verification'), async ({ request }) => {
        resentTo = ((await request.json()) as { email: string }).email
        return new HttpResponse(null, { status: 202 })
      }),
    )
    renderRoute(<LoginPage />, { path: '/login' })

    const user = await fillAndSubmit('new@acme.test', 'Correct-pass1')
    await user.click(await screen.findByRole('button', { name: 'Resend verification email' }))

    await waitFor(() => expect(resentTo).toBe('new@acme.test'))
  })
})
