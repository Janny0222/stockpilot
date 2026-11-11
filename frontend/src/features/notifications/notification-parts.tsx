import { ClipboardCheck, PackageCheck, PackageX, Truck, UserPlus, type LucideIcon } from 'lucide-react'
import type { ComponentProps } from 'react'
import { useNavigate } from 'react-router'
import type { NotificationDto, NotificationType } from '@/lib/api/types'
import { formatDateTime, timeAgo } from '@/lib/format'
import { entityPath } from '@/lib/links'
import { cn } from '@/lib/utils'
import { useMarkRead } from './api'

/** Icon per type; only stock problems wear a status colour (warning), everything else stays neutral. */
const TYPE_ICON: Record<NotificationType, { icon: LucideIcon; className: string }> = {
  LowStock: { icon: PackageX, className: 'bg-warning/12 text-warning' },
  PurchaseOrderAwaitingApproval: { icon: ClipboardCheck, className: 'bg-secondary text-secondary-foreground' },
  PurchaseOrderApproved: { icon: Truck, className: 'bg-secondary text-secondary-foreground' },
  SalesOrderReadyToShip: { icon: PackageCheck, className: 'bg-secondary text-secondary-foreground' },
  UserJoined: { icon: UserPlus, className: 'bg-secondary text-secondary-foreground' },
}

/**
 * One notification as a button: opening it marks it read and goes to what it's about. Unread ones are bold with a dot,
 * and the dot has a text alternative so the state isn't conveyed by colour or weight alone.
 */
export function NotificationRow({
  notification: n,
  onOpen,
  compact = false,
  className: extraClassName,
  onClick,
  ...buttonProps
}: { notification: NotificationDto; onOpen?: () => void; compact?: boolean } & Omit<ComponentProps<'button'>, 'type'>) {
  const navigate = useNavigate()
  const markRead = useMarkRead()
  const unread = !n.readAt
  const { icon: Icon, className } = TYPE_ICON[n.type] ?? TYPE_ICON.UserJoined
  const path = entityPath(n.entityType, n.entityId)

  const open = () => {
    if (unread) markRead.mutate(n.id)
    onOpen?.()
    if (path) navigate(path)
  }

  // Extra props come from a Radix menu item (asChild) inside the bell: keep its handlers, then open.
  return (
    <button
      type="button"
      {...buttonProps}
      onClick={(e) => {
        onClick?.(e)
        open()
      }}
      className={cn(
        'flex w-full items-start gap-3 rounded-lg px-3 py-3 text-left transition-colors outline-none hover:bg-accent focus-visible:ring-2 focus-visible:ring-ring data-[highlighted]:bg-accent',
        unread && 'bg-secondary/40',
        extraClassName,
      )}
    >
      <span className={cn('grid size-9 shrink-0 place-items-center rounded-lg', className)} aria-hidden="true">
        <Icon className="size-4.5" />
      </span>
      <span className="min-w-0 flex-1">
        <span className={cn('block text-sm', unread ? 'font-semibold' : 'font-medium text-muted-foreground')}>{n.title}</span>
        <span className={cn('mt-0.5 block text-sm text-muted-foreground', compact && 'line-clamp-2')}>{n.message}</span>
        <time dateTime={n.createdAt} title={formatDateTime(n.createdAt)} className="mt-1 block text-xs text-muted-foreground">
          {timeAgo(n.createdAt)}
        </time>
      </span>
      {unread && (
        <span className="mt-1.5 size-2 shrink-0 rounded-full bg-primary">
          <span className="sr-only">Unread</span>
        </span>
      )}
    </button>
  )
}
