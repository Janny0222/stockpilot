import { CircleCheck, OctagonX } from 'lucide-react'
import type { ReactNode } from 'react'
import { cn } from '@/lib/utils'

/** A form-level message (not tied to one field): "Email or password is incorrect.", "Link sent." */
export function FormAlert({ tone, children }: { tone: 'error' | 'success'; children: ReactNode }) {
  const Icon = tone === 'error' ? OctagonX : CircleCheck
  return (
    <div
      role={tone === 'error' ? 'alert' : 'status'}
      className={cn(
        'flex gap-3 rounded-lg border px-4 py-3 text-sm',
        tone === 'error' ? 'border-destructive/25 bg-destructive/5 text-destructive' : 'border-success/25 bg-success/5 text-success',
      )}
    >
      <Icon className="mt-0.5 size-4 shrink-0" aria-hidden="true" />
      <div className="text-foreground">{children}</div>
    </div>
  )
}
