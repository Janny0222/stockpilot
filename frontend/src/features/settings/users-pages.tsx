import { zodResolver } from '@hookform/resolvers/zod'
import { ArrowLeft, History, LoaderCircle, MailPlus, Search, Send, UserCheck, UserPlus, UserX, Users } from 'lucide-react'
import { useState } from 'react'
import { Controller, useForm, useWatch } from 'react-hook-form'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router'
import { toast } from 'sonner'
import { z } from 'zod'
import { ConfirmDialog } from '@/components/shared/confirm-dialog'
import { DataTable, Pagination, type Column, type SortState } from '@/components/shared/data-table'
import { FormField } from '@/components/shared/form-field'
import { EmptyState, ErrorState, PageHeader } from '@/components/shared/states'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { FormAlert } from '@/features/auth/components/form-alert'
import { useDebouncedValue } from '@/hooks/use-debounced-value'
import { applyAllFieldErrors, toApiError, type ApiError } from '@/lib/api/errors'
import type { RolesOverviewDto, UserDto, UserStatus } from '@/lib/api/types'
import { formatDateTime, formatDay } from '@/lib/format'
import { initials } from '@/lib/utils'
import { useInviteUser, useRoles, useUpdateUser, useUser, useUserAction, useUsers } from './api'
import { RoleField, UserStatusBadge, WarehousePicker } from './user-parts'

const nameRules = z.string().trim().min(1, 'Required.').max(100)
const accessSchema = z.object({ firstName: nameRules, lastName: nameRules, roleId: z.string().min(1, 'Choose a role.'), warehouseIds: z.array(z.string()) })
const inviteSchema = accessSchema.extend({ email: z.string().trim().min(1, 'Email is required.').email('Email is not valid.') })
type AccessValues = z.infer<typeof accessSchema>
type InviteValues = z.infer<typeof inviteSchema>

const STATUS_FILTERS: { value: UserStatus | 'all'; label: string }[] = [
  { value: 'all', label: 'All statuses' },
  { value: 'Active', label: 'Active' },
  { value: 'Invited', label: 'Invited' },
  { value: 'Deactivated', label: 'Deactivated' },
]

/** Warehouse-scoped roles need at least one warehouse; checked here for instant feedback (the API enforces it too). */
function needsWarehouse(roles: RolesOverviewDto | undefined, values: AccessValues) {
  const role = roles?.roles.find((r) => r.id === values.roleId)
  return role?.restrictToAssignedWarehouses && values.warehouseIds.length === 0
    ? `Pick at least one warehouse: ${role.name} only sees assigned warehouses.`
    : null
}

function scopeLabel(u: UserDto) {
  if (!u.restrictToAssignedWarehouses) return 'All warehouses'
  if (u.warehouses.length === 0) return 'No warehouses'
  return u.warehouses.map((w) => w.code).join(', ')
}

export function UsersPage() {
  const navigate = useNavigate()
  const [inviting, setInviting] = useState(false)
  const [params, setParams] = useSearchParams()
  const page = Math.max(1, Number(params.get('page')) || 1)
  const search = params.get('q') ?? ''
  const status = (params.get('status') as UserStatus | null) ?? undefined
  const sortParam = params.get('sort')
  const sort: SortState = sortParam ? { field: sortParam.split(':')[0]!, desc: sortParam.endsWith(':desc') } : null
  const debounced = useDebouncedValue(search)
  const query = useUsers({ page, pageSize: 20, search: debounced || undefined, status, sort: sortParam ?? undefined })

  const update = (changes: Record<string, string | null>) =>
    setParams(
      (prev) => {
        const next = new URLSearchParams(prev)
        for (const [k, v] of Object.entries(changes)) {
          if (v) next.set(k, v)
          else next.delete(k)
        }
        return next
      },
      { replace: true },
    )

  const columns: Column<UserDto>[] = [
    {
      key: 'name',
      header: 'Name',
      sortField: 'name',
      cell: (u) => (
        <div className="flex min-w-0 items-center gap-3">
          <span className="grid size-9 shrink-0 place-items-center rounded-full bg-secondary text-xs font-bold text-secondary-foreground" aria-hidden="true">
            {initials(u.firstName, u.lastName)}
          </span>
          <div className="min-w-0">
            <p className="truncate font-semibold">
              {u.firstName} {u.lastName}
              {u.isYou && <span className="ml-1.5 text-xs font-normal text-muted-foreground">(you)</span>}
            </p>
            <p className="truncate text-xs text-muted-foreground">{u.email}</p>
          </div>
        </div>
      ),
    },
    { key: 'role', header: 'Role', cell: (u) => u.roleName },
    { key: 'scope', header: 'Warehouses', hideOnMobile: true, cell: (u) => <span className="text-muted-foreground">{scopeLabel(u)}</span> },
    { key: 'status', header: 'Status', cell: (u) => <UserStatusBadge status={u.status} /> },
    {
      key: 'lastLogin',
      header: 'Last sign-in',
      sortField: 'lastLoginAt',
      hideOnMobile: true,
      cell: (u) => <span className="text-muted-foreground">{u.lastLoginAt ? formatDateTime(u.lastLoginAt) : 'Never'}</span>,
    },
  ]

  return (
    <>
      <PageHeader
        title="Users"
        description="Who can sign in to your company, what they can do, and where."
        actions={
          <Button onClick={() => setInviting(true)}>
            <UserPlus /> Invite user
          </Button>
        }
      />
      <Card className="gap-0 overflow-hidden py-0 shadow-card">
        <div className="flex flex-col gap-3 border-b p-4 sm:flex-row">
          <div className="relative max-w-sm flex-1">
            <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden="true" />
            <Input
              type="search"
              value={search}
              onChange={(e) => update({ q: e.target.value || null, page: null })}
              placeholder="Name or email"
              aria-label="Search users"
              className="h-10 pl-9"
            />
          </div>
          <Select value={status ?? 'all'} onValueChange={(v) => update({ status: v === 'all' ? null : v, page: null })}>
            <SelectTrigger className="h-10 w-full sm:w-44" aria-label="Filter by status">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {STATUS_FILTERS.map((s) => (
                <SelectItem key={s.value} value={s.value}>
                  {s.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        {query.isError ? (
          <ErrorState error={query.error} onRetry={() => query.refetch()} />
        ) : (
          <>
            <DataTable
              caption="Users"
              columns={columns}
              rows={query.data?.items}
              rowKey={(u) => u.id}
              isLoading={query.isPending}
              sort={sort}
              onSortChange={(s) => update({ sort: s ? `${s.field}:${s.desc ? 'desc' : 'asc'}` : null, page: null })}
              onRowClick={(u) => navigate(`/settings/users/${u.id}`)}
              empty={
                <EmptyState
                  icon={Users}
                  title={debounced || status ? 'No users match these filters' : 'Just you so far'}
                  description="Invite your team. Each person gets an email link to choose their own password."
                />
              }
            />
            {query.data && query.data.totalCount > 0 && (
              <Pagination
                page={query.data.page}
                pageSize={query.data.pageSize}
                totalCount={query.data.totalCount}
                onPageChange={(p) => update({ page: p > 1 ? String(p) : null })}
              />
            )}
          </>
        )}
      </Card>
      <Dialog open={inviting} onOpenChange={setInviting}>
        <DialogContent className="max-h-[90dvh] overflow-y-auto sm:max-w-xl">
          <DialogHeader>
            <DialogTitle>Invite a user</DialogTitle>
            <DialogDescription>They'll get an email link to choose their own password. The link works for 7 days.</DialogDescription>
          </DialogHeader>
          {/* Mounted only while open, so every invite starts from a clean form. */}
          {inviting && <InviteForm onDone={() => setInviting(false)} />}
        </DialogContent>
      </Dialog>
    </>
  )
}

function InviteForm({ onDone }: { onDone: () => void }) {
  const navigate = useNavigate()
  const roles = useRoles()
  const invite = useInviteUser()
  const form = useForm<InviteValues>({
    resolver: zodResolver(inviteSchema),
    defaultValues: { email: '', firstName: '', lastName: '', roleId: '', warehouseIds: [] },
  })

  const onSubmit = form.handleSubmit(async (values) => {
    const scopeError = needsWarehouse(roles.data, values)
    if (scopeError) return form.setError('warehouseIds', { message: scopeError })
    try {
      const user = await invite.mutateAsync(values)
      toast.success(`Invitation sent to ${user.email}.`)
      onDone()
      navigate(`/settings/users/${user.id}`)
    } catch (e) {
      const error = toApiError(e)
      if (error.code === 'EMAIL_TAKEN') form.setError('email', { type: 'server', message: error.message })
      else if (!applyAllFieldErrors(error, form.setError)) form.setError('root', { message: error.message })
    }
  })

  const { errors } = form.formState
  const roleId = useWatch({ control: form.control, name: 'roleId' })
  const restricted = roles.data?.roles.find((r) => r.id === roleId)?.restrictToAssignedWarehouses

  return (
    <>
      <form id="invite-form" noValidate onSubmit={onSubmit} className="grid gap-4 sm:grid-cols-2">
        {errors.root?.message && (
          <div className="sm:col-span-2">
            <FormAlert tone="error">{errors.root.message}</FormAlert>
          </div>
        )}
        <FormField label="Email" type="email" autoComplete="off" className="sm:col-span-2" registration={form.register('email')} error={errors.email} />
        <FormField label="First name" registration={form.register('firstName')} error={errors.firstName} />
        <FormField label="Last name" registration={form.register('lastName')} error={errors.lastName} />
        <div className="sm:col-span-2">
          {roles.isPending ? (
            <Skeleton className="h-16" />
          ) : roles.isError ? (
            <ErrorState error={roles.error} onRetry={() => roles.refetch()} className="py-4" />
          ) : (
            <Controller
              control={form.control}
              name="roleId"
              render={({ field }) => <RoleField roles={roles.data} value={field.value} onChange={field.onChange} error={errors.roleId?.message} />}
            />
          )}
        </div>
        {restricted && (
          <div className="sm:col-span-2">
            <Controller
              control={form.control}
              name="warehouseIds"
              render={({ field }) => <WarehousePicker value={field.value} onChange={field.onChange} error={errors.warehouseIds?.message} />}
            />
          </div>
        )}
      </form>
      <DialogFooter>
        <Button variant="outline" onClick={onDone}>
          Cancel
        </Button>
        <Button type="submit" form="invite-form" disabled={invite.isPending}>
          {invite.isPending ? <LoaderCircle className="animate-spin" aria-hidden="true" /> : <Send />}
          Send invitation
        </Button>
      </DialogFooter>
    </>
  )
}

export function UserDetailPage() {
  const { id = '' } = useParams()
  const query = useUser(id)
  const roles = useRoles()

  if (query.isPending || roles.isPending) {
    return (
      <div className="grid gap-6">
        <Skeleton className="h-16 max-w-md" />
        <Skeleton className="h-96 max-w-3xl" />
      </div>
    )
  }
  if (query.isError) return <ErrorState error={query.error} onRetry={() => query.refetch()} />
  if (roles.isError) return <ErrorState error={roles.error} onRetry={() => roles.refetch()} />

  const user = query.data
  return (
    <>
      <Button asChild variant="ghost" size="sm" className="mb-3 -ml-2 text-muted-foreground">
        <Link to="/settings/users">
          <ArrowLeft /> All users
        </Link>
      </Button>
      <PageHeader
        title={`${user.firstName} ${user.lastName}`}
        description={
          <span className="inline-flex flex-wrap items-center gap-2">
            {user.email} <UserStatusBadge status={user.status} />
          </span>
        }
        actions={<UserActions user={user} />}
      />
      <div className="grid max-w-5xl gap-6 lg:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
        {/* Keyed so a save (or someone else's change) re-seeds the form from the server. */}
        <AccessForm key={`${user.roleId}|${user.firstName}|${user.lastName}|${user.warehouses.map((w) => w.id).join()}`} user={user} roles={roles.data} />
        <Card className="h-fit shadow-card">
          <CardHeader>
            <CardTitle>Activity</CardTitle>
          </CardHeader>
          <CardContent className="grid gap-4 text-sm">
            <dl className="grid gap-3">
              <div className="flex justify-between gap-3">
                <dt className="text-muted-foreground">Last sign-in</dt>
                <dd className="font-medium">{user.lastLoginAt ? formatDateTime(user.lastLoginAt) : 'Never'}</dd>
              </div>
              <div className="flex justify-between gap-3">
                <dt className="text-muted-foreground">{user.status === 'Invited' ? 'Invited' : 'Added'}</dt>
                <dd className="font-medium">{formatDay(user.createdAt)}</dd>
              </div>
            </dl>
            <Button asChild variant="outline" size="sm" className="justify-self-start">
              <Link to={`/settings/audit-log?userId=${user.id}`}>
                <History /> See what they changed
              </Link>
            </Button>
          </CardContent>
        </Card>
      </div>
    </>
  )
}

function UserActions({ user }: { user: UserDto }) {
  const action = useUserAction(user.id)
  const run = (kind: 'deactivate' | 'activate' | 'resend-invitation', message: string) =>
    action.mutate(kind, { onSuccess: () => toast.success(message), onError: (e) => toast.error(toApiError(e).message) })

  if (user.isYou) return null
  return (
    <div className="flex flex-wrap gap-2">
      {user.status === 'Invited' && (
        <Button variant="outline" onClick={() => run('resend-invitation', `New invitation sent to ${user.email}.`)} disabled={action.isPending}>
          <MailPlus /> Resend invitation
        </Button>
      )}
      {user.status === 'Deactivated' ? (
        <Button variant="outline" onClick={() => run('activate', `${user.firstName} can sign in again.`)} disabled={action.isPending}>
          <UserCheck /> Reactivate
        </Button>
      ) : (
        <ConfirmDialog
          trigger={
            <Button variant="outline" className="text-destructive hover:text-destructive" disabled={action.isPending}>
              <UserX /> Deactivate
            </Button>
          }
          title={`Deactivate ${user.firstName} ${user.lastName}?`}
          description="They're signed out on every device right away and can't sign in again until you reactivate them. Everything they recorded stays, with their name on it."
          confirmLabel="Deactivate"
          destructive
          onConfirm={() => run('deactivate', `${user.firstName} has been deactivated.`)}
        />
      )}
    </div>
  )
}

function AccessForm({ user, roles }: { user: UserDto; roles: RolesOverviewDto }) {
  const save = useUpdateUser(user.id)
  const form = useForm<AccessValues>({
    resolver: zodResolver(accessSchema),
    defaultValues: { firstName: user.firstName, lastName: user.lastName, roleId: user.roleId, warehouseIds: user.warehouses.map((w) => w.id) },
  })

  const onSubmit = form.handleSubmit(async (values) => {
    const scopeError = needsWarehouse(roles, values)
    if (scopeError) return form.setError('warehouseIds', { message: scopeError })
    try {
      await save.mutateAsync(values)
      toast.success(values.roleId !== user.roleId ? 'Saved. Their new role takes effect within a minute; they stay signed in.' : 'Saved.')
    } catch (e) {
      const error: ApiError = toApiError(e)
      if (!applyAllFieldErrors(error, form.setError)) form.setError('root', { message: error.message })
    }
  })

  const { errors, isDirty } = form.formState
  const roleId = useWatch({ control: form.control, name: 'roleId' })
  const restricted = roles.roles.find((r) => r.id === roleId)?.restrictToAssignedWarehouses

  return (
    <Card className="shadow-card">
      <CardHeader>
        <CardTitle>Access</CardTitle>
        <CardDescription>Their role decides what they can do; warehouses decide where, for warehouse-scoped roles.</CardDescription>
      </CardHeader>
      <CardContent>
        <form noValidate onSubmit={onSubmit} className="grid gap-4 sm:grid-cols-2">
          {errors.root?.message && (
            <div className="sm:col-span-2">
              <FormAlert tone="error">{errors.root.message}</FormAlert>
            </div>
          )}
          <FormField label="First name" registration={form.register('firstName')} error={errors.firstName} />
          <FormField label="Last name" registration={form.register('lastName')} error={errors.lastName} />
          <div className="sm:col-span-2">
            <Controller
              control={form.control}
              name="roleId"
              render={({ field }) => (
                <RoleField
                  roles={roles}
                  value={field.value}
                  onChange={field.onChange}
                  disabled={user.isYou}
                  error={errors.roleId?.message}
                  hint={user.isYou ? "You can't change your own role. Another Owner can." : undefined}
                />
              )}
            />
          </div>
          {restricted && (
            <div className="sm:col-span-2">
              <Controller
                control={form.control}
                name="warehouseIds"
                render={({ field }) => <WarehousePicker value={field.value} onChange={field.onChange} error={errors.warehouseIds?.message} />}
              />
            </div>
          )}
          <div className="flex gap-3 sm:col-span-2">
            <Button type="submit" disabled={save.isPending || !isDirty}>
              {save.isPending && <LoaderCircle className="animate-spin" aria-hidden="true" />}
              Save changes
            </Button>
            {isDirty && (
              <Button type="button" variant="ghost" onClick={() => form.reset()}>
                Discard
              </Button>
            )}
          </div>
        </form>
      </CardContent>
    </Card>
  )
}
