import { ArrowRight, Building, ShieldCheck, Warehouse } from 'lucide-react'
import { Link } from 'react-router'
import { visibleNav, tenantNav } from '@/app/navigation'
import { PageHeader } from '@/components/shared/states'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { useWarehouses } from '@/features/warehouses/api'
import { Permissions as P } from '@/lib/permissions'
import { useAuthStore, useHasPermission } from '@/stores/auth-store'

function greeting() {
  const h = new Date().getHours()
  return h < 12 ? 'Good morning' : h < 18 ? 'Good afternoon' : 'Good evening'
}

/**
 * Phase 3 dashboard: only real data that exists today (profile, company, warehouses).
 * Sales, stock value and low-stock metrics arrive in Phase 4 with the modules that produce them.
 * No placeholder numbers.
 */
export function DashboardPage() {
  const user = useAuthStore((s) => s.user)!
  const canSeeWarehouses = useHasPermission(P.Warehouses.View)
  const modules = visibleNav(tenantNav, user.permissions).filter((m) => m.to !== '/')

  return (
    <>
      <PageHeader
        title={`${greeting()}, ${user.firstName}`}
        description={user.tenant ? `Here's where ${user.tenant.name} stands today.` : 'Platform administration'}
      />

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
        <StatCard icon={Building} tone="violet" label="Company" value={user.tenant?.name ?? 'Platform'} sub={user.tenant ? `Currency ${user.tenant.currency}` : undefined} />
        <StatCard icon={ShieldCheck} tone="blue" label="Your role" value={user.role.name} sub={`${user.permissions.length} permissions`} />
        {canSeeWarehouses && <WarehouseStat restricted={user.warehouseScope.restricted} />}
      </div>

      {modules.length > 0 && (
        <Card className="mt-6 shadow-card">
          <CardHeader>
            <CardTitle>Your workspace</CardTitle>
          </CardHeader>
          <CardContent>
            <ul className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
              {modules.map((m) => (
                <li key={m.to}>
                  <Link
                    to={m.to}
                    className="group flex items-center gap-3 rounded-lg border p-3.5 transition-colors hover:border-primary/40 hover:bg-secondary/40 focus-visible:outline-2 focus-visible:outline-ring"
                  >
                    <span className="grid size-9 place-items-center rounded-lg bg-secondary text-secondary-foreground">
                      <m.icon className="size-4" aria-hidden="true" />
                    </span>
                    <span className="flex-1">
                      <span className="block font-semibold">{m.label}</span>
                      <span className="block text-xs text-muted-foreground">{m.available ? 'Open' : 'Coming in the next release'}</span>
                    </span>
                    <ArrowRight className="size-4 text-muted-foreground transition-transform group-hover:translate-x-0.5" aria-hidden="true" />
                  </Link>
                </li>
              ))}
            </ul>
          </CardContent>
        </Card>
      )}
    </>
  )
}

function WarehouseStat({ restricted }: { restricted: boolean }) {
  const query = useWarehouses({ page: 1, pageSize: 1 })
  return (
    <StatCard
      icon={Warehouse}
      tone="green"
      label={restricted ? 'Your warehouses' : 'Warehouses'}
      value={query.isPending ? null : query.isError ? '—' : String(query.data.totalCount)}
      sub={restricted ? 'Assigned to you' : 'Across your company'}
      href="/warehouses"
    />
  )
}

const TONES = {
  violet: 'bg-[#efebff] text-[#5a3cf0]',
  blue: 'bg-[#e6f0ff] text-[#2563eb]',
  green: 'bg-[#e3f6ee] text-[#0e8a60]',
} as const

function StatCard({
  icon: Icon,
  tone,
  label,
  value,
  sub,
  href,
}: {
  icon: typeof Building
  tone: keyof typeof TONES
  label: string
  value: string | null
  sub?: string
  href?: string
}) {
  const body = (
    <Card className="h-full shadow-card transition-colors">
      <CardContent className="flex items-start gap-4">
        <span className={`grid size-12 shrink-0 place-items-center rounded-xl ${TONES[tone]}`}>
          <Icon className="size-6" aria-hidden="true" />
        </span>
        <div className="min-w-0">
          <p className="text-sm text-muted-foreground">{label}</p>
          {value === null ? (
            <Skeleton className="mt-1.5 h-7 w-20" />
          ) : (
            <p className="truncate text-2xl font-bold tracking-tight tabular">{value}</p>
          )}
          {sub && <p className="mt-0.5 text-sm text-muted-foreground">{sub}</p>}
        </div>
      </CardContent>
    </Card>
  )
  return href ? (
    <Link to={href} className="rounded-xl focus-visible:outline-2 focus-visible:outline-ring [&>div]:hover:border-primary/40">
      {body}
    </Link>
  ) : (
    body
  )
}
