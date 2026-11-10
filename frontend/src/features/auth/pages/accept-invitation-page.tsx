import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { LoaderCircle } from 'lucide-react'
import { useForm } from 'react-hook-form'
import { Link, useNavigate } from 'react-router'
import { toast } from 'sonner'
import { FormField } from '@/components/shared/form-field'
import { Button } from '@/components/ui/button'
import { toApiError, type ApiError } from '@/lib/api/errors'
import { useAuthStore } from '@/stores/auth-store'
import { authApi } from '../api'
import { AuthLayout } from '../components/auth-layout'
import { FormAlert } from '../components/form-alert'
import { resetPasswordSchema, type ResetPasswordValues } from '../schemas'
import { useHashToken } from '../use-hash-token'

const backToSignIn = (
  <Link to="/login" className="font-semibold text-primary hover:underline">
    Back to sign in
  </Link>
)

/** Opened from the invitation email: /accept-invitation#token=… */
export function AcceptInvitationPage() {
  const token = useHashToken()
  const invitation = useQuery({
    queryKey: ['invitation', token],
    queryFn: () => authApi.invitation(token!),
    enabled: !!token,
    retry: false,
    staleTime: Infinity,
  })

  if (!token || invitation.isError) {
    return (
      <AuthLayout title="This invitation doesn't work" footer={backToSignIn}>
        <p className="text-muted-foreground">
          {invitation.error ? toApiError(invitation.error).message : 'The invitation link is missing its token.'} Ask the person who invited you to send a new
          one from Settings, Users.
        </p>
      </AuthLayout>
    )
  }

  if (invitation.isPending) {
    return (
      <AuthLayout title="Opening your invitation">
        <p className="flex items-center gap-2 text-muted-foreground" role="status">
          <LoaderCircle className="size-4 animate-spin" aria-hidden="true" /> One moment…
        </p>
      </AuthLayout>
    )
  }

  const { data } = invitation
  return (
    <AuthLayout
      title={`Join ${data.companyName}`}
      description={
        <>
          You're joining as <strong className="text-foreground">{data.roleName}</strong>. Choose a password to finish setting up your account.
        </>
      }
    >
      <AcceptForm token={token} email={data.email} firstName={data.firstName} />
    </AuthLayout>
  )
}

function AcceptForm({ token, email, firstName }: { token: string; email: string; firstName: string }) {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const form = useForm<ResetPasswordValues>({ resolver: zodResolver(resetPasswordSchema), defaultValues: { newPassword: '', confirmPassword: '' } })

  const accept = useMutation<void, ApiError, ResetPasswordValues>({
    mutationFn: async ({ newPassword }) => {
      try {
        // Someone else may still be signed in on this browser. Drop their token first, so the request goes out
        // anonymous and the audit log credits the change to the new user, not to them.
        useAuthStore.getState().setAccessToken(null)
        const { accessToken } = await authApi.acceptInvitation({ token, password: newPassword })
        // Whoever was signed in on this browser before is replaced by the new account.
        queryClient.clear()
        useAuthStore.getState().setAccessToken(accessToken)
        useAuthStore.getState().setUser(await authApi.me())
      } catch (e) {
        throw toApiError(e)
      }
    },
    onSuccess: () => {
      toast.success(`Welcome, ${firstName}!`)
      navigate('/', { replace: true })
    },
  })

  return (
    <form noValidate onSubmit={form.handleSubmit((v) => accept.mutate(v))} className="grid gap-5">
      {accept.error && <FormAlert tone="error">{accept.error.message}</FormAlert>}
      {/* Visible, read-only username so password managers save the right account. */}
      <div className="grid gap-1.5">
        <span className="text-sm font-medium">Email</span>
        <input
          type="email"
          readOnly
          value={email}
          autoComplete="username"
          aria-label="Email"
          className="h-10 rounded-md border bg-muted px-3 text-muted-foreground"
        />
      </div>
      <FormField
        label="Password"
        type="password"
        autoComplete="new-password"
        autoFocus
        registration={form.register('newPassword')}
        error={form.formState.errors.newPassword}
        hint="At least 8 characters, with an uppercase letter, a lowercase letter and a number."
      />
      <FormField
        label="Confirm password"
        type="password"
        autoComplete="new-password"
        registration={form.register('confirmPassword')}
        error={form.formState.errors.confirmPassword}
      />
      <Button type="submit" size="lg" className="h-11 w-full" disabled={accept.isPending}>
        {accept.isPending && <LoaderCircle className="animate-spin" aria-hidden="true" />}
        Join and sign in
      </Button>
    </form>
  )
}
