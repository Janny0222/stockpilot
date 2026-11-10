import { zodResolver } from '@hookform/resolvers/zod'
import { LoaderCircle, Lock } from 'lucide-react'
import { useMemo } from 'react'
import { Controller, useForm } from 'react-hook-form'
import { toast } from 'sonner'
import { z } from 'zod'
import { FormField } from '@/components/shared/form-field'
import { ErrorState, PageHeader } from '@/components/shared/states'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { FormAlert } from '@/features/auth/components/form-alert'
import { CURRENCIES } from '@/features/auth/schemas'
import { applyAllFieldErrors, toApiError } from '@/lib/api/errors'
import type { CompanySettingsDto } from '@/lib/api/types'
import { formatDay } from '@/lib/format'
import { useCompanySettings, useUpdateCompanySettings } from './api'

const companySchema = z.object({
  name: z.string().trim().min(2, 'Company name must be at least 2 characters.').max(120),
  currency: z.string().regex(/^[A-Z]{3}$/, 'Pick a currency.'),
  timeZone: z
    .string()
    .trim()
    .min(1, 'Time zone is required.')
    .regex(/^[A-Za-z_]+(\/[A-Za-z0-9_+-]+)*$/, 'Use a time zone name such as Asia/Manila.'),
  allowNegativeStock: z.boolean(),
})
type CompanyValues = z.infer<typeof companySchema>

function timeZones() {
  try {
    return Intl.supportedValuesOf('timeZone')
  } catch {
    return ['UTC', 'Asia/Manila', 'Asia/Singapore', 'Asia/Tokyo', 'Europe/London', 'America/New_York']
  }
}

export function CompanySettingsPage() {
  const query = useCompanySettings()

  return (
    <>
      <PageHeader title="Company" description="Your company's name and the defaults every report and stock rule uses." />
      {query.isPending ? (
        <Skeleton className="h-96 max-w-3xl" />
      ) : query.isError ? (
        <ErrorState error={query.error} onRetry={() => query.refetch()} />
      ) : (
        // Keyed by version-ish fields so a save re-seeds the form with what the server stored.
        <CompanyForm key={`${query.data.name}|${query.data.currency}|${query.data.timeZone}|${query.data.allowNegativeStock}`} settings={query.data} />
      )}
    </>
  )
}

function CompanyForm({ settings }: { settings: CompanySettingsDto }) {
  const update = useUpdateCompanySettings()
  const zones = useMemo(() => timeZones(), [])
  const currencies = useMemo(() => Array.from(new Set<string>([...CURRENCIES, settings.currency])), [settings.currency])
  const form = useForm<CompanyValues>({
    resolver: zodResolver(companySchema),
    defaultValues: {
      name: settings.name,
      currency: settings.currency,
      timeZone: settings.timeZone,
      allowNegativeStock: settings.allowNegativeStock,
    },
  })

  const onSubmit = form.handleSubmit(async (values) => {
    try {
      await update.mutateAsync(values)
      toast.success('Company settings saved.')
    } catch (e) {
      const error = toApiError(e)
      if (error.code === 'CURRENCY_LOCKED') form.setError('currency', { type: 'server', message: error.message })
      else if (!applyAllFieldErrors(error, form.setError)) form.setError('root', { message: error.message })
    }
  })

  const { errors, isDirty } = form.formState
  return (
    <form noValidate onSubmit={onSubmit} className="grid max-w-3xl gap-6">
      {errors.root?.message && <FormAlert tone="error">{errors.root.message}</FormAlert>}

      <Card className="shadow-card">
        <CardHeader>
          <CardTitle>Profile</CardTitle>
          <CardDescription>Shown in the app header and in invitation emails.</CardDescription>
        </CardHeader>
        <CardContent className="grid gap-5">
          <FormField label="Company name" registration={form.register('name')} error={errors.name} />
          <dl className="grid gap-3 text-sm sm:grid-cols-2">
            <div>
              <dt className="text-muted-foreground">Company ID</dt>
              <dd className="mt-0.5 font-medium">{settings.slug}</dd>
            </div>
            <div>
              <dt className="text-muted-foreground">Registered</dt>
              <dd className="mt-0.5 font-medium">{formatDay(settings.createdAt)}</dd>
            </div>
          </dl>
        </CardContent>
      </Card>

      <Card className="shadow-card">
        <CardHeader>
          <CardTitle>Money and time</CardTitle>
          <CardDescription>Reports count days in this time zone, so a sale at 1:30 AM belongs to that day.</CardDescription>
        </CardHeader>
        <CardContent className="grid gap-5 sm:grid-cols-2">
          <div className="grid content-start gap-1.5">
            <Label htmlFor="company-currency">Currency</Label>
            <Controller
              control={form.control}
              name="currency"
              render={({ field }) => (
                <Select value={field.value} onValueChange={field.onChange} disabled={settings.currencyLocked}>
                  <SelectTrigger id="company-currency" className="h-10 w-full bg-card" aria-invalid={errors.currency ? true : undefined}>
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {currencies.map((c) => (
                      <SelectItem key={c} value={c}>
                        {c}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            />
            {errors.currency?.message ? (
              <p role="alert" className="text-sm text-destructive">
                {errors.currency.message}
              </p>
            ) : settings.currencyLocked ? (
              <p className="flex items-start gap-1.5 text-sm text-muted-foreground">
                <Lock className="mt-0.5 size-3.5 shrink-0" aria-hidden="true" />
                Locked: stock has already moved, and every cost and price is stored in {settings.currency}.
              </p>
            ) : (
              <p className="text-sm text-muted-foreground">You can change this until the first stock movement.</p>
            )}
          </div>
          <FormField
            label="Time zone"
            list="company-time-zones"
            autoComplete="off"
            spellCheck={false}
            registration={form.register('timeZone')}
            error={errors.timeZone}
            hint="Start typing a city, e.g. Manila."
          />
          <datalist id="company-time-zones">
            {zones.map((z) => (
              <option key={z} value={z} />
            ))}
          </datalist>
        </CardContent>
      </Card>

      <Card className="shadow-card">
        <CardHeader>
          <CardTitle>Stock rules</CardTitle>
        </CardHeader>
        <CardContent>
          <label className="flex cursor-pointer items-start gap-3">
            <input type="checkbox" className="mt-1 size-4 accent-primary" {...form.register('allowNegativeStock')} />
            <span>
              <span className="font-medium">Allow negative stock</span>
              <span className="mt-0.5 block text-sm text-muted-foreground">
                Lets staff ship or issue items the system thinks you don't have, and fix the count later. Leave it off unless your stock counts often lag behind
                real shipments.
              </span>
            </span>
          </label>
        </CardContent>
      </Card>

      <div className="flex gap-3">
        <Button type="submit" disabled={update.isPending || !isDirty}>
          {update.isPending && <LoaderCircle className="animate-spin" aria-hidden="true" />}
          Save changes
        </Button>
        {isDirty && (
          <Button type="button" variant="ghost" onClick={() => form.reset()}>
            Discard
          </Button>
        )}
      </div>
    </form>
  )
}
