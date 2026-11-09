import { clsx, type ClassValue } from 'clsx'
import { twMerge } from 'tailwind-merge'

/** Merge Tailwind classes, letting later ones win: cn('px-2', isBig && 'px-4') → 'px-4'. */
export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs))
}

/** User-facing initials for avatars: "Maria Santos" → "MS". */
export function initials(firstName?: string | null, lastName?: string | null) {
  return `${firstName?.[0] ?? ''}${lastName?.[0] ?? ''}`.toUpperCase() || '?'
}
