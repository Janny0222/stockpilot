import { isAxiosError } from 'axios'
import type { FieldValues, Path, UseFormSetError } from 'react-hook-form'

/**
 * Every API failure, normalized. The backend returns RFC 9457 ProblemDetails:
 *   { status, title, detail, code: "EMAIL_TAKEN", errors?: { email: ["…"] }, traceId }
 * `code` is stable and safe to branch on; `detail` is a human-readable message.
 */
export class ApiError extends Error {
  readonly status: number
  readonly code: string
  readonly fieldErrors: Record<string, string[]>
  readonly traceId?: string

  constructor(status: number, code: string, message: string, fieldErrors: Record<string, string[]> = {}, traceId?: string) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.code = code
    this.fieldErrors = fieldErrors
    this.traceId = traceId
  }
}

type ProblemBody = {
  status?: number
  title?: string
  detail?: string
  code?: string
  errors?: Record<string, string[]>
  traceId?: string
}

export function toApiError(error: unknown): ApiError {
  if (error instanceof ApiError) return error

  if (isAxiosError<ProblemBody>(error)) {
    if (!error.response) {
      return new ApiError(0, 'NETWORK_ERROR', "Can't reach the server. Check your connection and try again.")
    }
    const { status, data } = error.response
    return new ApiError(
      status,
      data?.code ?? `HTTP_${status}`,
      data?.detail ?? data?.title ?? defaultMessage(status),
      data?.errors ?? {},
      data?.traceId,
    )
  }

  return new ApiError(0, 'UNKNOWN', error instanceof Error ? error.message : 'Something went wrong.')
}

function defaultMessage(status: number) {
  if (status === 403) return "You don't have permission to do that."
  if (status === 404) return "That item doesn't exist or you don't have access to it."
  if (status === 429) return "You're doing that too often. Wait a moment and try again."
  if (status >= 500) return 'The server ran into a problem. Try again in a moment.'
  return 'Something went wrong.'
}

/** "variants[0].sku" (server) → "variants.0.sku" (React Hook Form path). */
export const toFormPath = (serverKey: string) => serverKey.replace(/\[(\d+)\]/g, '.$1')

/**
 * Applies EVERY server field error to the form (for forms with dynamic fields such as variant rows).
 * Returns true when at least one was applied.
 */
export function applyAllFieldErrors<T extends FieldValues>(error: ApiError, setError: UseFormSetError<T>) {
  const entries = Object.entries(error.fieldErrors).filter(([, messages]) => messages.length > 0)
  for (const [key, messages] of entries) {
    setError(toFormPath(key) as Path<T>, { type: 'server', message: messages[0] })
  }
  return entries.length > 0
}

/**
 * Pushes server-side validation errors onto the matching form fields, so a rule the server
 * enforces but the client doesn't (e.g. "email already taken") shows next to the right input.
 * Returns true when at least one field error was applied.
 */
export function applyFieldErrors<T extends FieldValues>(error: ApiError, setError: UseFormSetError<T>, fields: readonly Path<T>[]) {
  let applied = false
  for (const field of fields) {
    const messages = error.fieldErrors[field]
    if (messages?.length) {
      setError(field, { type: 'server', message: messages[0] })
      applied = true
    }
  }
  return applied
}
