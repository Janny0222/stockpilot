import { Building2, History, UserRound, Users, type LucideIcon } from 'lucide-react'
import { NavLink, Outlet } from 'react-router'
import { Permissions as P } from '@/lib/permissions'
import { cn } from '@/lib/utils'
import { useAuthStore } from '@/stores/auth-store'

type SettingsTab = { to: string; label: string; icon: LucideIcon; permission?: string }

const tabs: SettingsTab[] = [
  { to: '/settings/account', label: 'Your account', icon: UserRound },
  { to: '/settings/company', label: 'Company', icon: Building2, permission: P.Company.Manage },
  { to: '/settings/users', label: 'Users', icon: Users, permission: P.Users.Manage },
  { to: '/settings/audit-log', label: 'Audit log', icon: History, permission: P.Audit.View },
]

/**
 * Settings sub-navigation. Everyone has "Your account"; the company-wide sections appear only for roles that may open
 * them (Owner by default). With a single section there's nothing to switch between, so the strip is hidden.
 */
export function SettingsLayout() {
  const permissions = useAuthStore((s) => s.user?.permissions ?? [])
  const visible = tabs.filter((t) => !t.permission || permissions.includes(t.permission))

  return (
    <>
      {visible.length > 1 && (
        // The divider is an inset shadow, not a border, so the active tab's underline can sit on it without the
        // negative margin that would make this horizontally scrollable strip scroll vertically too.
        <nav aria-label="Settings sections" className="-mx-1 mb-6 overflow-x-auto shadow-[inset_0_-1px_0_var(--border)]">
          <ul className="flex min-w-max gap-1 px-1">
            {visible.map((tab) => (
              <li key={tab.to}>
                <NavLink
                  to={tab.to}
                  className={({ isActive }) =>
                    cn(
                      'inline-flex h-11 items-center gap-2 border-b-2 px-3 text-sm font-medium transition-colors focus-visible:outline-2 focus-visible:outline-ring',
                      isActive ? 'border-primary text-foreground' : 'border-transparent text-muted-foreground hover:text-foreground',
                    )
                  }
                >
                  <tab.icon className="size-4" aria-hidden="true" />
                  {tab.label}
                </NavLink>
              </li>
            ))}
          </ul>
        </nav>
      )}
      <Outlet />
    </>
  )
}
