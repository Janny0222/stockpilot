import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation } from '@tanstack/react-query'
import { LoaderCircle, MailCheck } from 'lucide-react'
import { useEffect, useRef } from 'react'
import { useForm } from 'react-hook-form'
import { Link, useLocation, useNavigate } from 'react-router'
import { toast } from 'sonner'
import { FormField } from '@/components/shared/form-field'
import { Button } from '@/components/ui/button'
import { toApiError, type ApiError } from '@/lib/api/errors'
import { authApi } from '../api'
import { AuthLayout } from '../components/auth-layout'
import { FormAlert } from '../components/form-alert'
import { emailOnlySchema, resetPasswordSchema, type EmailOnlyValues, type ResetPasswordValues } from '../schemas'
import { useHashToken } from '../use-hash-token'

const backToSignIn = (
  <Link to="/login" className="font-semibold text-primary hover:underline">
    Back to sign in
  </Link>
)

/** Shown right after registration. */
export function CheckEmailPage() {
  const email = (useLocation().state as { email?: string } | null)?.email
  const resend = useMutation({
    mutationFn: () => authApi.resendVerification(email!),
    onSuccess: () => toast.success('Verification email sent again.'),
    onError: (e) => toast.error(toApiError(e).message),
  })

  return (
    <AuthLayout title="Check your inbox" footer={backToSignIn}>
      <div className="grid gap-5">
        <div className="grid size-12 place-items-center rounded-xl bg-secondary text-secondary-foreground">
          <MailCheck className="size-6" aria-hidden="true" />
        </div>
        <p className="text-muted-foreground">
          We sent a verification link to {email ? <strong className="text-foreground">{email}</strong> : 'your email'}. Open it to
          activate your account. The link expires in 24 hours.
        </p>
        {email && (
          <Button variant="outline" className="h-11 w-full" onClick={() => resend.mutate()} disabled={resend.isPending}>
            {resend.isPending && <LoaderCircle className="animate-spin" aria-hidden="true" />}
            Resend verification email
          </Button>
        )}
      </div>
    </AuthLayout>
  )
}

/** Opened from the email link: /verify-email#token=… */
export function VerifyEmailPage() {
  const token = useHashToken()
  const verify = useMutation<void, ApiError>({
    mutationFn: async () => {
      await authApi.verifyEmail(token!).catch((e) => {
        throw toApiError(e)
      })
    },
  })

  // React StrictMode runs effects twice in development; a single-use token must be sent once.
  const started = useRef(false)
  useEffect(() => {
    if (token && !started.current) {
      started.current = true
      verify.mutate()
    }
  }, [token, verify])

  if (!token || verify.isError) {
    return (
      <AuthLayout title="This link doesn't work" footer={backToSignIn}>
        <p className="mb-6 text-muted-foreground">
          {verify.error?.message ?? 'The verification link is missing its token.'} Enter your email and we'll send a new link.
        </p>
        <ResendForm />
      </AuthLayout>
    )
  }

  if (verify.isSuccess) {
    return (
      <AuthLayout title="Email verified" description="Your account is active. Sign in to set up your company.">
        <Button asChild size="lg" className="h-11 w-full">
          <Link to="/login">Sign in</Link>
        </Button>
      </AuthLayout>
    )
  }

  return (
    <AuthLayout title="Verifying your email">
      <p className="flex items-center gap-2 text-muted-foreground" role="status">
        <LoaderCircle className="size-4 animate-spin" aria-hidden="true" /> One moment…
      </p>
    </AuthLayout>
  )
}

function ResendForm() {
  const form = useForm<EmailOnlyValues>({ resolver: zodResolver(emailOnlySchema), defaultValues: { email: '' } })
  const resend = useMutation({ mutationFn: (v: EmailOnlyValues) => authApi.resendVerification(v.email) })

  if (resend.isSuccess) {
    return <FormAlert tone="success">If that account still needs verifying, a new link is on its way.</FormAlert>
  }
  return (
    <form noValidate onSubmit={form.handleSubmit((v) => resend.mutate(v))} className="grid gap-5">
      <FormField label="Email" type="email" autoComplete="email" registration={form.register('email')} error={form.formState.errors.email} />
      <Button type="submit" className="h-11 w-full" disabled={resend.isPending}>
        Send new link
      </Button>
    </form>
  )
}

export function ForgotPasswordPage() {
  const form = useForm<EmailOnlyValues>({ resolver: zodResolver(emailOnlySchema), defaultValues: { email: '' } })
  const request = useMutation<void, ApiError, EmailOnlyValues>({
    mutationFn: async ({ email }) => {
      await authApi.forgotPassword(email).catch((e) => {
        throw toApiError(e)
      })
    },
  })

  return (
    <AuthLayout
      title="Reset your password"
      description="Enter the email you sign in with and we'll send you a reset link."
      footer={backToSignIn}
    >
      {request.isSuccess ? (
        // Same message whether or not the account exists, matching the API (no account enumeration).
        <FormAlert tone="success">
          If an account exists for <strong>{form.getValues('email')}</strong>, a reset link is on its way. It expires in 60 minutes.
        </FormAlert>
      ) : (
        <form noValidate onSubmit={form.handleSubmit((v) => request.mutate(v))} className="grid gap-5">
          {request.error && <FormAlert tone="error">{request.error.message}</FormAlert>}
          <FormField label="Email" type="email" autoComplete="email" autoFocus registration={form.register('email')} error={form.formState.errors.email} />
          <Button type="submit" size="lg" className="h-11 w-full" disabled={request.isPending}>
            {request.isPending && <LoaderCircle className="animate-spin" aria-hidden="true" />}
            Send reset link
          </Button>
        </form>
      )}
    </AuthLayout>
  )
}

/** Opened from the email link: /reset-password#token=… */
export function ResetPasswordPage() {
  const token = useHashToken()
  const navigate = useNavigate()
  const form = useForm<ResetPasswordValues>({
    resolver: zodResolver(resetPasswordSchema),
    defaultValues: { newPassword: '', confirmPassword: '' },
  })

  const reset = useMutation<void, ApiError, ResetPasswordValues>({
    mutationFn: async ({ newPassword }) => {
      await authApi.resetPassword({ token: token!, newPassword }).catch((e) => {
        throw toApiError(e)
      })
    },
    onSuccess: () => {
      toast.success('Password changed. Sign in with your new password.')
      navigate('/login', { replace: true })
    },
  })

  if (!token) {
    return (
      <AuthLayout title="This link doesn't work" footer={backToSignIn}>
        <p className="mb-6 text-muted-foreground">The reset link is missing its token. Request a new one.</p>
        <Button asChild className="h-11 w-full">
          <Link to="/forgot-password">Request a new link</Link>
        </Button>
      </AuthLayout>
    )
  }

  return (
    <AuthLayout title="Choose a new password" description="You'll be signed out on every device after changing it." footer={backToSignIn}>
      <form noValidate onSubmit={form.handleSubmit((v) => reset.mutate(v))} className="grid gap-5">
        {reset.error && (
          <FormAlert tone="error">
            {reset.error.message}{' '}
            {reset.error.code === 'TOKEN_INVALID' && (
              <Link to="/forgot-password" className="font-semibold text-primary hover:underline">
                Request a new link
              </Link>
            )}
          </FormAlert>
        )}
        <FormField
          label="New password"
          type="password"
          autoComplete="new-password"
          autoFocus
          registration={form.register('newPassword')}
          error={form.formState.errors.newPassword}
          hint="At least 8 characters, with an uppercase letter, a lowercase letter and a number."
        />
        <FormField
          label="Confirm new password"
          type="password"
          autoComplete="new-password"
          registration={form.register('confirmPassword')}
          error={form.formState.errors.confirmPassword}
        />
        <Button type="submit" size="lg" className="h-11 w-full" disabled={reset.isPending}>
          {reset.isPending && <LoaderCircle className="animate-spin" aria-hidden="true" />}
          Change password
        </Button>
      </form>
    </AuthLayout>
  )
}
