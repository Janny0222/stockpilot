import { Building, ChevronRight, Menu } from 'lucide-react'
import { useState } from 'react'
import { Link, Outlet, useMatches } from 'react-router'
import { Button } from '@/components/ui/button'
import { Sheet, SheetContent, SheetDescription, SheetTitle } from '@/components/ui/sheet'
import { NotificationBell } from '@/features/notifications/notification-bell'
import { useAuthStore } from '@/stores/auth-store'
import { Sidebar } from './sidebar'

export type RouteHandle = { crumb?: string }

/**
 * Desktop (≥1024px): fixed sidebar + header + scrolling content.
 * Tablet/phone: the sidebar becomes a slide-in drawer opened from the header.
 */
export function AppShell() {
  const [mobileOpen, setMobileOpen] = useState(false)

  return (
    <div className="min-h-svh bg-background">
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:fixed focus:top-3 focus:left-3 focus:z-50 focus:rounded-md focus:bg-card focus:px-4 focus:py-2 focus:shadow-pop"
      >
        Skip to content
      </a>

      <aside className="fixed inset-y-0 left-0 z-30 hidden w-64 lg:block">
        <Sidebar />
      </aside>

      <Sheet open={mobileOpen} onOpenChange={setMobileOpen}>
        <SheetContent side="left" className="w-72 border-none p-0">
          <SheetTitle className="sr-only">Navigation</SheetTitle>
          <SheetDescription className="sr-only">Main navigation and account menu</SheetDescription>
          <Sidebar onNavigate={() => setMobileOpen(false)} />
        </SheetContent>
      </Sheet>

      <div className="lg:pl-64">
        <header className="sticky top-0 z-20 flex h-16 items-center gap-3 border-b bg-background/85 px-4 backdrop-blur sm:px-6 lg:px-8">
          <Button variant="ghost" size="icon" className="lg:hidden" onClick={() => setMobileOpen(true)} aria-label="Open navigation">
            <Menu />
          </Button>
          <Breadcrumbs />
          <NotificationBell />
          <CompanyBadge />
        </header>

        <main id="main" className="px-4 py-6 sm:px-6 lg:px-8 lg:py-8">
          <Outlet />
        </main>
      </div>
    </div>
  )
}

function Breadcrumbs() {
  const crumbs = useMatches()
    .filter((m) => (m.handle as RouteHandle | undefined)?.crumb)
    .map((m) => ({ path: m.pathname, label: (m.handle as RouteHandle).crumb! }))

  return (
    <nav aria-label="Breadcrumb" className="min-w-0 flex-1">
      <ol className="flex items-center gap-1.5 text-sm">
        {crumbs.map((c, i) => {
          const last = i === crumbs.length - 1
          return (
            <li key={c.path} className="flex min-w-0 items-center gap-1.5">
              {i > 0 && <ChevronRight className="size-3.5 shrink-0 text-muted-foreground" aria-hidden="true" />}
              {last ? (
                <span aria-current="page" className="truncate font-semibold text-foreground">
                  {c.label}
                </span>
              ) : (
                <Link to={c.path} className="truncate text-muted-foreground hover:text-foreground">
                  {c.label}
                </Link>
              )}
            </li>
          )
        })}
      </ol>
    </nav>
  )
}

/** One user belongs to one company (assumption A1), so this is a label, not a switcher. */
function CompanyBadge() {
  const tenant = useAuthStore((s) => s.user?.tenant)
  const isPlatformAdmin = useAuthStore((s) => s.user?.isPlatformAdmin)
  const label = tenant?.name ?? (isPlatformAdmin ? 'Platform administration' : null)
  if (!label) return null

  return (
    <div className="hidden items-center gap-2 rounded-lg border bg-card px-3 py-1.5 text-sm font-medium shadow-card sm:flex">
      <Building className="size-4 text-muted-foreground" aria-hidden="true" />
      <span className="max-w-48 truncate">{label}</span>
    </div>
  )
}
