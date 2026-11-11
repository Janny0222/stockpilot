import { Bell, BellOff, CheckCheck, LoaderCircle } from 'lucide-react'
import { DropdownMenu as MenuPrimitive } from 'radix-ui'
import { useState } from 'react'
import { Link } from 'react-router'
import { Button } from '@/components/ui/button'
import { DropdownMenu, DropdownMenuContent, DropdownMenuTrigger } from '@/components/ui/dropdown-menu'
import { useMarkAllRead, useNotifications, useUnreadCount } from './api'
import { NotificationRow } from './notification-parts'

const PREVIEW = 6

/** "9+" keeps the badge small; the button's accessible name always has the real number. */
const badge = (count: number) => (count > 9 ? '9+' : String(count))

const footerLink = 'flex-1 px-4 py-3 text-center text-sm font-medium outline-none hover:bg-accent data-[highlighted]:bg-accent'

/**
 * The header bell. Every row, "Mark all read" and the footer links are menu items (Radix primitives, asChild), so
 * arrow keys move between them like any menu, while each keeps its own look.
 */
export function NotificationBell() {
  const [open, setOpen] = useState(false)
  const unread = useUnreadCount()
  // The list loads only while the menu is open, and refreshes each time it opens.
  const list = useNotifications({ page: 1, pageSize: PREVIEW }, open)
  const markAll = useMarkAllRead()
  const count = unread.data ?? 0

  // No company (platform admin) → the query never runs and there's no inbox to show.
  if (unread.isError || (unread.isPending && unread.fetchStatus === 'idle')) return null

  return (
    <DropdownMenu open={open} onOpenChange={setOpen}>
      <DropdownMenuTrigger asChild>
        <Button variant="ghost" size="icon" className="relative" aria-label={count > 0 ? `Notifications, ${count} unread` : 'Notifications'}>
          <Bell />
          {count > 0 && (
            <span
              className="absolute -top-0.5 -right-0.5 grid h-4.5 min-w-4.5 place-items-center rounded-full bg-primary px-1 text-[0.65rem] leading-none font-bold text-primary-foreground tabular ring-2 ring-background"
              aria-hidden="true"
            >
              {badge(count)}
            </span>
          )}
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-[min(24rem,calc(100vw-2rem))] p-0">
        <div className="flex items-center justify-between gap-3 border-b px-4 py-2.5">
          <p className="font-semibold">Notifications</p>
          {count > 0 && (
            <MenuPrimitive.Item asChild onSelect={(e) => e.preventDefault()}>
              <Button
                variant="ghost"
                size="sm"
                className="h-8 text-muted-foreground data-[highlighted]:bg-accent"
                onClick={() => markAll.mutate()}
                disabled={markAll.isPending}
              >
                <CheckCheck /> Mark all read
              </Button>
            </MenuPrimitive.Item>
          )}
        </div>
        <div className="max-h-[min(28rem,65dvh)] overflow-y-auto p-1.5">
          {list.isPending ? (
            <p className="flex items-center justify-center gap-2 py-8 text-sm text-muted-foreground" role="status">
              <LoaderCircle className="size-4 animate-spin" aria-hidden="true" /> Loading…
            </p>
          ) : list.isError ? (
            <p className="py-8 text-center text-sm text-muted-foreground">Couldn't load notifications. Try again in a moment.</p>
          ) : list.data.items.length === 0 ? (
            <div className="grid justify-items-center gap-2 px-6 py-10 text-center">
              <BellOff className="size-6 text-muted-foreground" aria-hidden="true" />
              <p className="font-medium">You're all caught up</p>
              <p className="text-sm text-muted-foreground">Low stock, approvals and orders to ship will show up here.</p>
            </div>
          ) : (
            <div className="grid gap-0.5">
              {list.data.items.map((n) => (
                <MenuPrimitive.Item key={n.id} asChild>
                  <NotificationRow notification={n} compact />
                </MenuPrimitive.Item>
              ))}
            </div>
          )}
        </div>
        <div className="flex border-t">
          <MenuPrimitive.Item asChild>
            <Link to="/notifications" className={`${footerLink} text-primary`}>
              View all
            </Link>
          </MenuPrimitive.Item>
          <MenuPrimitive.Item asChild>
            <Link to="/settings/notifications" className={`${footerLink} border-l text-muted-foreground`}>
              Preferences
            </Link>
          </MenuPrimitive.Item>
        </div>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}
