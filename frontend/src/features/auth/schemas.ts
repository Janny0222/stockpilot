import { z } from 'zod'

/**
 * Client-side rules mirror the server's FluentValidation (ValidationRules.cs) so users get instant
 * feedback. They are a convenience, not a guarantee: the server re-validates everything.
 */
export const passwordRule = z
  .string()
  .min(8, 'Use at least 8 characters.')
  .max(128, 'Use at most 128 characters.')
  .regex(/[A-Z]/, 'Add an uppercase letter.')
  .regex(/[a-z]/, 'Add a lowercase letter.')
  .regex(/[0-9]/, 'Add a number.')

const email = z.string().trim().min(1, 'Enter your email.').email('Enter a valid email address.')

export const loginSchema = z.object({
  email,
  password: z.string().min(1, 'Enter your password.'),
})
export type LoginValues = z.infer<typeof loginSchema>

export const CURRENCIES = ['PHP', 'USD', 'EUR', 'SGD', 'JPY', 'AUD', 'GBP'] as const

export const registerSchema = z.object({
  companyName: z.string().trim().min(2, 'Use at least 2 characters.').max(120),
  firstName: z.string().trim().min(1, 'Enter your first name.').max(100),
  lastName: z.string().trim().min(1, 'Enter your last name.').max(100),
  email,
  password: passwordRule,
  currency: z.enum(CURRENCIES),
})
export type RegisterValues = z.infer<typeof registerSchema>

export const emailOnlySchema = z.object({ email })
export type EmailOnlyValues = z.infer<typeof emailOnlySchema>

export const resetPasswordSchema = z
  .object({
    newPassword: passwordRule,
    confirmPassword: z.string(),
  })
  .refine((v) => v.newPassword === v.confirmPassword, {
    path: ['confirmPassword'],
    message: "Passwords don't match.",
  })
export type ResetPasswordValues = z.infer<typeof resetPasswordSchema>
