import { ClipboardList, Lock, UsersRound } from 'lucide-react'
import type { ReactNode } from 'react'
import { Link } from 'react-router'
import { Logo } from '@/components/shared/logo'

const POINTS = [
  { icon: ClipboardList, text: 'Every stock movement is recorded, so counts always add up.' },
  { icon: Lock, text: "Your company's data is visible only to your team." },
  { icon: UsersRound, text: 'Roles decide who can receive, sell, adjust or approve.' },
]

export function AuthLayout({ title, description, children, footer }: { title: string; description?: ReactNode; children: ReactNode; footer?: ReactNode }) {
  return (
    <div className="grid min-h-svh lg:grid-cols-[minmax(0,5fr)_minmax(0,7fr)]">
      <aside className="relative hidden overflow-hidden bg-sidebar p-12 text-sidebar-foreground lg:flex lg:flex-col">
        <Logo inverted />
        <div className="mt-auto max-w-md">
          <p className="text-[2.1rem] leading-[1.15] font-bold tracking-tight text-white">
            Know what's on every shelf, in every warehouse.
          </p>
          <ul className="mt-8 grid gap-4">
            {POINTS.map(({ icon: Icon, text }) => (
              <li key={text} className="flex gap-3">
                <span className="grid size-8 shrink-0 place-items-center rounded-lg bg-sidebar-accent text-white">
                  <Icon className="size-4" aria-hidden="true" />
                </span>
                <span className="pt-1 leading-relaxed">{text}</span>
              </li>
            ))}
          </ul>
        </div>
        <ShelfGrid />
      </aside>

      <main className="flex flex-col px-5 py-8 sm:px-10">
        <Link to="/login" className="lg:hidden">
          <Logo />
        </Link>
        <div className="mx-auto flex w-full max-w-[26rem] flex-1 flex-col justify-center py-10">
          <h1 className="text-[1.75rem] font-bold tracking-tight">{title}</h1>
          {description && <p className="mt-2 text-muted-foreground">{description}</p>}
          <div className="mt-8">{children}</div>
          {footer && <div className="mt-8 text-sm text-muted-foreground">{footer}</div>}
        </div>
      </main>
    </div>
  )
}

/** Quiet background motif: a warehouse rack seen from above, a grid of bins with a few occupied. */
function ShelfGrid() {
  const filled = new Set([3, 4, 9, 14, 15, 16, 22, 27, 28, 33])
  return (
    <svg className="pointer-events-none absolute -top-8 -right-10 w-80 opacity-[0.16]" viewBox="0 0 240 240" aria-hidden="true">
      {Array.from({ length: 36 }, (_, i) => (
        <rect
          key={i}
          x={(i % 6) * 40 + 4}
          y={Math.floor(i / 6) * 40 + 4}
          width="32"
          height="32"
          rx="6"
          fill={filled.has(i) ? '#8b74ff' : 'none'}
          stroke="#8b74ff"
          strokeWidth="1.5"
        />
      ))}
    </svg>
  )
}
