import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation } from '@tanstack/react-query'
import { LoaderCircle } from 'lucide-react'
import { useForm } from 'react-hook-form'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { toast } from 'sonner'
import { safeReturnTo } from '@/app/guards'
import { FormField } from '@/components/shared/form-field'
import { Button } from '@/components/ui/button'
import { toApiError, type ApiError } from '@/lib/api/errors'
import { authApi } from '../api'
import { AuthLayout } from '../components/auth-layout'
import { FormAlert } from '../components/form-alert'
import { loginSchema, type LoginValues } from '../schemas'
import { signIn } from '../session'

export function LoginPage() {
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const form = useForm<LoginValues>({ resolver: zodResolver(loginSchema), defaultValues: { email: '', password: '' } })

  const login = useMutation<void, ApiError, LoginValues>({
    mutationFn: async ({ email, password }) => {
      try {
        await signIn(email, password)
      } catch (e) {
        throw toApiError(e)
      }
    },
    onSuccess: () => navigate(safeReturnTo(params.get('returnTo')), { replace: true }),
  })

  const resend = useMutation({
    mutationFn: () => authApi.resendVerification(form.getValues('email')),
    onSuccess: () => toast.success('Verification email sent. Check your inbox.'),
    onError: (e) => toast.error(toApiError(e).message),
  })

  return (
    <AuthLayout
      title="Sign in to StockPilot"
      description="Welcome back. Enter your work email to continue."
      footer={
        <>
          New to StockPilot?{' '}
          <Link to="/register" className="font-semibold text-primary hover:underline">
            Create a company account
          </Link>
        </>
      }
    >
      <form noValidate onSubmit={form.handleSubmit((v) => login.mutate(v))} className="grid gap-5">
        {login.error && (
          <FormAlert tone="error">
            {login.error.message}
            {login.error.code === 'EMAIL_NOT_VERIFIED' && (
              <Button
                type="button"
                variant="link"
                className="h-auto p-0 pl-1 align-baseline"
                onClick={() => resend.mutate()}
                disabled={resend.isPending}
              >
                Resend verification email
              </Button>
            )}
            {login.error.code === 'ACCOUNT_LOCKED' && (
              <Link to="/forgot-password" className="block pt-1 font-semibold text-primary hover:underline">
                Reset your password
              </Link>
            )}
          </FormAlert>
        )}

        <FormField
          label="Email"
          type="email"
          autoComplete="email"
          autoFocus
          registration={form.register('email')}
          error={form.formState.errors.email}
        />
        <FormField
          label="Password"
          type="password"
          autoComplete="current-password"
          registration={form.register('password')}
          error={form.formState.errors.password}
          labelAction={
            <Link to="/forgot-password" className="text-sm font-medium text-primary hover:underline">
              Forgot password?
            </Link>
          }
        />

        <Button type="submit" size="lg" className="h-11 w-full text-[0.95rem]" disabled={login.isPending}>
          {login.isPending && <LoaderCircle className="animate-spin" aria-hidden="true" />}
          Sign in
        </Button>
      </form>

      {import.meta.env.DEV && <DemoAccounts onPick={(email) => form.reset({ email, password: 'Demo@12345' })} />}
    </AuthLayout>
  )
}

/** Development only: one-click fill of the seeded demo users (the build strips this in production). */
function DemoAccounts({ onPick }: { onPick: (email: string) => void }) {
  const accounts = [
    ['Owner', 'owner@demo.stockpilot.app'],
    ['Manager', 'manager@demo.stockpilot.app'],
    ['Inventory', 'inventory@demo.stockpilot.app'],
    ['Sales', 'sales@demo.stockpilot.app'],
  ] as const
  return (
    <div className="mt-8 rounded-lg border border-dashed p-4">
      <p className="text-sm font-semibold">Demo accounts</p>
      <p className="mt-0.5 text-sm text-muted-foreground">Development build only. Fills in the seeded user.</p>
      <div className="mt-3 flex flex-wrap gap-2">
        {accounts.map(([role, email]) => (
          <Button key={email} type="button" variant="secondary" size="sm" onClick={() => onPick(email)}>
            {role}
          </Button>
        ))}
      </div>
    </div>
  )
}
