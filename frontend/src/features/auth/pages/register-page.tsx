import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation } from '@tanstack/react-query'
import { LoaderCircle } from 'lucide-react'
import { Controller, useForm } from 'react-hook-form'
import { Link, useNavigate } from 'react-router'
import { toast } from 'sonner'
import { FormField } from '@/components/shared/form-field'
import { Button } from '@/components/ui/button'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { applyFieldErrors, toApiError, type ApiError } from '@/lib/api/errors'
import type { RegisterCompanyResult } from '@/lib/api/types'
import { authApi } from '../api'
import { AuthLayout } from '../components/auth-layout'
import { FormAlert } from '../components/form-alert'
import { CURRENCIES, registerSchema, type RegisterValues } from '../schemas'

const FIELDS = ['companyName', 'firstName', 'lastName', 'email', 'password', 'currency'] as const

function detectTimeZone() {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC'
  } catch {
    return 'UTC'
  }
}

export function RegisterPage() {
  const navigate = useNavigate()
  const form = useForm<RegisterValues>({
    resolver: zodResolver(registerSchema),
    defaultValues: { companyName: '', firstName: '', lastName: '', email: '', password: '', currency: 'PHP' },
  })

  const register = useMutation<RegisterCompanyResult, ApiError, RegisterValues>({
    mutationFn: (values) =>
      authApi.register({ ...values, timeZone: detectTimeZone() }).catch((e) => {
        throw toApiError(e)
      }),
    onSuccess: (result, values) => {
      if (result.emailVerificationRequired) {
        navigate('/register/check-email', { state: { email: values.email } })
      } else {
        toast.success('Company created. Sign in to continue.')
        navigate('/login')
      }
    },
    onError: (error) => {
      // "Email already registered" belongs next to the email field, not in a banner.
      if (error.code === 'EMAIL_TAKEN') {
        form.setError('email', { type: 'server', message: error.message })
      } else {
        applyFieldErrors(error, form.setError, FIELDS)
      }
    },
  })

  const { errors } = form.formState
  const showBanner = register.error && register.error.code !== 'EMAIL_TAKEN' && Object.keys(register.error.fieldErrors).length === 0

  return (
    <AuthLayout
      title="Create your company account"
      description="You'll be the owner. You can invite your team after signing in."
      footer={
        <>
          Already have an account?{' '}
          <Link to="/login" className="font-semibold text-primary hover:underline">
            Sign in
          </Link>
        </>
      }
    >
      <form noValidate onSubmit={form.handleSubmit((v) => register.mutate(v))} className="grid gap-5">
        {showBanner && <FormAlert tone="error">{register.error!.message}</FormAlert>}

        <FormField label="Company name" autoComplete="organization" autoFocus registration={form.register('companyName')} error={errors.companyName} />

        <div className="grid gap-5 sm:grid-cols-2">
          <FormField label="First name" autoComplete="given-name" registration={form.register('firstName')} error={errors.firstName} />
          <FormField label="Last name" autoComplete="family-name" registration={form.register('lastName')} error={errors.lastName} />
        </div>

        <FormField label="Work email" type="email" autoComplete="email" registration={form.register('email')} error={errors.email} />

        <FormField
          label="Password"
          type="password"
          autoComplete="new-password"
          registration={form.register('password')}
          error={errors.password}
          hint="At least 8 characters, with an uppercase letter, a lowercase letter and a number."
        />

        <div className="grid gap-1.5">
          <Label htmlFor="currency">Currency</Label>
          <Controller
            control={form.control}
            name="currency"
            render={({ field }) => (
              <Select value={field.value} onValueChange={field.onChange}>
                <SelectTrigger id="currency" className="h-10 w-full bg-card">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {CURRENCIES.map((c) => (
                    <SelectItem key={c} value={c}>
                      {c}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            )}
          />
          <p className="text-sm text-muted-foreground">Used for prices and inventory value. One currency per company.</p>
        </div>

        <Button type="submit" size="lg" className="h-11 w-full text-[0.95rem]" disabled={register.isPending}>
          {register.isPending && <LoaderCircle className="animate-spin" aria-hidden="true" />}
          Create company
        </Button>
      </form>
    </AuthLayout>
  )
}
