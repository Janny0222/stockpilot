import { cn } from '@/lib/utils'

/** Brand mark: an isometric box (inventory) drawn as one continuous route (pilot). */
export function LogoMark({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 32 32" aria-hidden="true" className={cn('size-8 shrink-0', className)}>
      <rect width="32" height="32" rx="9" className="fill-primary" />
      <path
        d="M16 6.5 24.5 11v10L16 25.5 7.5 21V11L16 6.5Z M7.8 11.2 16 15.6l8.2-4.4 M16 15.6v9.6"
        fill="none"
        stroke="white"
        strokeWidth="2.2"
        strokeLinejoin="round"
      />
    </svg>
  )
}

export function Logo({ className, inverted = false }: { className?: string; inverted?: boolean }) {
  return (
    <span className={cn('inline-flex items-center gap-2.5', className)}>
      <LogoMark />
      <span className={cn('text-lg font-extrabold tracking-tight', inverted ? 'text-white' : 'text-foreground')}>
        StockPilot
      </span>
    </span>
  )
}
