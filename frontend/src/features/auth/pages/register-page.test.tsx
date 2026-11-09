import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { renderRoute } from '@/test/render'
import { apiUrl, problem, server } from '@/test/server'
import { RegisterPage } from './register-page'

async function fillForm(password = 'Str0ng-Pass') {
  const user = userEvent.setup()
  await user.type(screen.getByLabelText('Company name'), 'Acme Trading')
  await user.type(screen.getByLabelText('First name'), 'Maria')
  await user.type(screen.getByLabelText('Last name'), 'Santos')
  await user.type(screen.getByLabelText('Work email'), 'maria@acme.test')
  await user.type(screen.getByLabelText('Password'), password)
  await user.click(screen.getByRole('button', { name: 'Create company' }))
}

describe('RegisterPage', () => {
  it('explains which password rule is not met', async () => {
    renderRoute(<RegisterPage />, { path: '/register' })

    await fillForm('alllowercase')

    expect(await screen.findByText('Add an uppercase letter.')).toBeInTheDocument()
  })

  it('sends the form plus the browser time zone, then asks the user to check their inbox', async () => {
    let body: Record<string, unknown> = {}
    server.use(
      http.post(apiUrl('/auth/register'), async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>
        return HttpResponse.json({ tenantId: 't', userId: 'u', emailVerificationRequired: true }, { status: 201 })
      }),
    )
    renderRoute(<RegisterPage />, { path: '/register' })

    await fillForm()

    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent('/register/check-email'))
    expect(body).toMatchObject({ companyName: 'Acme Trading', email: 'maria@acme.test', currency: 'PHP' })
    expect(typeof body.timeZone).toBe('string')
  })

  it('shows "email already registered" on the email field itself', async () => {
    server.use(http.post(apiUrl('/auth/register'), () => problem(409, 'EMAIL_TAKEN', 'An account with this email already exists.')))
    renderRoute(<RegisterPage />, { path: '/register' })

    await fillForm()

    expect(await screen.findByText('An account with this email already exists.')).toBeInTheDocument()
    expect(screen.getByLabelText('Work email')).toHaveAttribute('aria-invalid', 'true')
  })

  it('maps server-side validation errors onto the matching fields', async () => {
    server.use(
      http.post(apiUrl('/auth/register'), () =>
        problem(400, 'VALIDATION_FAILED', 'One or more fields are invalid.', { companyName: ['That company name is not allowed.'] }),
      ),
    )
    renderRoute(<RegisterPage />, { path: '/register' })

    await fillForm()

    expect(await screen.findByText('That company name is not allowed.')).toBeInTheDocument()
    expect(screen.getByLabelText('Company name')).toHaveAttribute('aria-invalid', 'true')
  })
})
