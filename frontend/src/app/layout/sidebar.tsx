import { ChevronsUpDown, LogOut, UserRound } from 'lucide-react'
import { Link, NavLink } from 'react-router'
import { Logo } from '@/components/shared/logo'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { useSignOut } from '@/features/auth/session'
import { cn, initials } from '@/lib/utils'
import { useAuthStore } from '@/stores/auth-store'
import { platformNav, settingsNav, tenantNav, visibleNav, type NavItem } from '../navigation'

export function Sidebar({ onNavigate }: { onNavigate?: () => void }) {
  const user = useAuthStore((s) => s.user)
  const permissions = user?.permissions ?? []
  const main = visibleNav(tenantNav, permissions)
  const platform = visibleNav(platformNav, permissions)

  return (
    <div className="flex h-full flex-col bg-sidebar text-sidebar-foreground">
      <div className="flex h-16 items-center px-5">
        <Link to="/" onClick={onNavigate} className="rounded-md focus-visible:outline-2 focus-visible:outline-sidebar-primary">
          <Logo inverted />
        </Link>
      </div>

      <nav aria-label="Main" className="flex-1 overflow-y-auto px-3 py-4">
        {main.length > 0 && <NavList items={main} onNavigate={onNavigate} />}
        {platform.length > 0 && (
          <>
            <p className="mt-6 mb-2 px-3 text-xs font-semibold text-sidebar-muted">Platform</p>
            <NavList items={platform} onNavigate={onNavigate} />
          </>
        )}
        <div className="mt-6 border-t border-sidebar-border pt-4">
          <NavList items={[settingsNav]} onNavigate={onNavigate} />
        </div>
      </nav>

      <UserMenu onNavigate={onNavigate} />
    </div>
  )
}

function NavList({ items, onNavigate }: { items: NavItem[]; onNavigate?: () => void }) {
  return (
    <ul className="grid gap-1">
      {items.map((item) => (
        <li key={item.to}>
          <NavLink
            to={item.to}
            end={item.to === '/'}
            onClick={onNavigate}
            className={({ isActive }) =>
              cn(
                'group flex h-11 items-center gap-3 rounded-lg px-3 text-[0.925rem] font-medium transition-colors',
                'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-sidebar-primary',
                isActive
                  ? 'bg-sidebar-primary text-sidebar-primary-foreground shadow-[0_6px_16px_-6px_rgb(90_60_240/0.7)]'
                  : 'hover:bg-sidebar-accent hover:text-white',
              )
            }
          >
            <item.icon className="size-[1.15rem] shrink-0" aria-hidden="true" />
            <span className="flex-1 truncate">{item.label}</span>
            {!item.available && (
              <span className="rounded-full border border-sidebar-border px-2 py-0.5 text-[0.7rem] font-medium text-sidebar-muted">
                Soon
              </span>
            )}
          </NavLink>
        </li>
      ))}
    </ul>
  )
}

function UserMenu({ onNavigate }: { onNavigate?: () => void }) {
  const user = useAuthStore((s) => s.user)
  const signOut = useSignOut()
  if (!user) return null

  return (
    <div className="border-t border-sidebar-border p-3">
      <DropdownMenu>
        <DropdownMenuTrigger className="flex w-full items-center gap-3 rounded-lg p-2 text-left hover:bg-sidebar-accent focus-visible:outline-2 focus-visible:outline-sidebar-primary">
          <span className="grid size-9 shrink-0 place-items-center rounded-full bg-sidebar-accent text-sm font-bold text-white">
            {initials(user.firstName, user.lastName)}
          </span>
          <span className="min-w-0 flex-1">
            <span className="block truncate text-sm font-semibold text-white">
              {user.firstName} {user.lastName}
            </span>
            <span className="block truncate text-xs text-sidebar-muted">{user.role.name}</span>
          </span>
          <ChevronsUpDown className="size-4 text-sidebar-muted" aria-hidden="true" />
        </DropdownMenuTrigger>
        <DropdownMenuContent side="top" align="start" className="w-60">
          <DropdownMenuLabel className="font-normal">
            <span className="block truncate text-sm font-semibold">{user.email}</span>
            {user.tenant && <span className="block truncate text-xs text-muted-foreground">{user.tenant.name}</span>}
          </DropdownMenuLabel>
          <DropdownMenuSeparator />
          <DropdownMenuItem asChild>
            <Link to="/settings/account" onClick={onNavigate}>
              <UserRound /> Account & sessions
            </Link>
          </DropdownMenuItem>
          <DropdownMenuItem onSelect={() => signOut.mutate(false)} disabled={signOut.isPending}>
            <LogOut /> Sign out
          </DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>
    </div>
  )
}
