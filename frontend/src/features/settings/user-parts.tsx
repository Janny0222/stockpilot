import { Badge } from '@/components/ui/badge'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import type { RoleOptionDto, RolesOverviewDto, UserStatus } from '@/lib/api/types'
import { cn } from '@/lib/utils'
import { useActiveWarehouses } from '@/features/warehouses/api'
import { USER_STATUS } from './api'

export function UserStatusBadge({ status }: { status: UserStatus }) {
  const s = USER_STATUS[status]
  return (
    <Badge variant="outline" className={s.className}>
      {s.label}
    </Badge>
  )
}

/** One line per role in the picker, so the Owner picks by what people DO, not by a bare title. */
const ROLE_HINTS: Record<string, string> = {
  Owner: 'Everything, including users, company settings and the audit log.',
  Manager: 'Runs day-to-day operations: stock, purchasing, sales and reports.',
  'Inventory Staff': 'Receives, moves and ships stock in their assigned warehouses only.',
  'Sales Staff': 'Customers and sales orders. Never sees costs or margins.',
}

export function RoleSelect({
  id,
  roles,
  value,
  onChange,
  disabled,
  invalid,
}: {
  id: string
  roles: RoleOptionDto[]
  value: string
  onChange: (value: string) => void
  disabled?: boolean
  invalid?: boolean
}) {
  return (
    <Select value={value} onValueChange={onChange} disabled={disabled}>
      <SelectTrigger id={id} className="h-10 w-full bg-card" aria-invalid={invalid ? true : undefined}>
        {/* Explicit children: the trigger shows just the name, not the item's two-line hint. */}
        <SelectValue placeholder="Choose a role">{roles.find((r) => r.id === value)?.name}</SelectValue>
      </SelectTrigger>
      <SelectContent>
        {roles.map((r) => (
          <SelectItem key={r.id} value={r.id} className="py-2">
            <span className="grid text-left">
              <span className="font-medium">{r.name}</span>
              {ROLE_HINTS[r.name] && <span className="text-xs text-muted-foreground">{ROLE_HINTS[r.name]}</span>}
            </span>
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  )
}

/** Checkbox list of the company's active warehouses (≤100), used for warehouse-scoped roles. */
export function WarehousePicker({ value, onChange, error }: { value: string[]; onChange: (ids: string[]) => void; error?: string }) {
  const warehouses = useActiveWarehouses()
  const toggle = (id: string) => onChange(value.includes(id) ? value.filter((v) => v !== id) : [...value, id])

  return (
    <fieldset className="grid gap-2" aria-describedby={error ? 'warehouse-picker-error' : undefined}>
      <legend className="mb-1 text-sm font-medium">Warehouses they can work in</legend>
      {warehouses.isPending ? (
        <p className="text-sm text-muted-foreground">Loading warehouses…</p>
      ) : (
        <ul className="grid grid-cols-[repeat(auto-fill,minmax(13rem,1fr))] gap-2">
          {warehouses.data?.items.map((w) => {
            const checked = value.includes(w.id)
            return (
              <li key={w.id}>
                <label
                  className={cn(
                    'flex cursor-pointer items-center gap-3 rounded-lg border bg-card px-3 py-2.5 text-sm transition-colors',
                    checked ? 'border-primary/50 bg-secondary/60' : 'hover:bg-accent',
                  )}
                >
                  <input type="checkbox" className="size-4 accent-primary" checked={checked} onChange={() => toggle(w.id)} />
                  <span className="min-w-0">
                    <span className="block truncate font-medium">{w.name}</span>
                    <span className="text-xs text-muted-foreground">{w.code}</span>
                  </span>
                </label>
              </li>
            )
          })}
        </ul>
      )}
      {error && (
        <p id="warehouse-picker-error" role="alert" className="text-sm text-destructive">
          {error}
        </p>
      )}
    </fieldset>
  )
}

/** Read-only "what this role can do", grouped like the permission catalogue. */
export function RolePermissions({ role, overview }: { role: RoleOptionDto | undefined; overview: RolesOverviewDto }) {
  if (!role) return null
  const granted = new Set(role.permissions)
  const groups = new Map<string, string[]>()
  for (const p of overview.permissions) {
    if (!granted.has(p.code)) continue
    groups.set(p.group, [...(groups.get(p.group) ?? []), p.description])
  }

  return (
    <details className="group rounded-lg border bg-muted/40 px-4 py-3 text-sm">
      <summary className="cursor-pointer font-medium marker:text-muted-foreground">What the {role.name} role can do</summary>
      <dl className="mt-3 grid gap-3 sm:grid-cols-2">
        {[...groups.entries()].map(([group, items]) => (
          <div key={group}>
            <dt className="font-medium">{group}</dt>
            <dd>
              <ul className="mt-1 grid gap-0.5 text-muted-foreground">
                {items.map((i) => (
                  <li key={i}>{i}</li>
                ))}
              </ul>
            </dd>
          </div>
        ))}
      </dl>
    </details>
  )
}

export function RoleField({
  roles,
  value,
  onChange,
  disabled,
  error,
  hint,
}: {
  roles: RolesOverviewDto
  value: string
  onChange: (value: string) => void
  disabled?: boolean
  error?: string
  hint?: string
}) {
  return (
    <div className="grid gap-1.5">
      <Label htmlFor="user-role">Role</Label>
      <RoleSelect id="user-role" roles={roles.roles} value={value} onChange={onChange} disabled={disabled} invalid={!!error} />
      {error ? (
        <p role="alert" className="text-sm text-destructive">
          {error}
        </p>
      ) : (
        hint && <p className="text-sm text-muted-foreground">{hint}</p>
      )}
      <RolePermissions role={roles.roles.find((r) => r.id === value)} overview={roles} />
    </div>
  )
}
