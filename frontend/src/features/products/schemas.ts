import { z } from 'zod'

/** Mirrors Product / ProductVariant rules in the backend (Domain + FluentValidation). */
export const UNITS = ['pcs', 'box', 'pack', 'set', 'pair', 'roll', 'kg', 'g', 'L', 'mL', 'm', 'cm'] as const

const money = (label: string) =>
  z
    .number({ error: `Enter the ${label}.` })
    .min(0, `${label[0]!.toUpperCase()}${label.slice(1)} can't be negative.`)
    .max(999_999_999, 'That amount is too large.')

export const variantSchema = z.object({
  sku: z
    .string()
    .trim()
    .min(1, 'Enter a SKU.')
    .max(40, 'Use at most 40 characters.')
    .regex(/^[A-Za-z0-9._/-]+$/, 'Letters, numbers and . _ / - only.'),
  name: z.string().trim().max(100).optional(),
  barcode: z
    .string()
    .trim()
    .max(64)
    .regex(/^[A-Za-z0-9-]*$/, 'Letters, numbers and - only.')
    .optional(),
  costPrice: money('cost'),
  sellingPrice: money('selling price'),
  reorderLevel: z.number({ error: 'Enter a reorder level.' }).min(0, "Can't be negative."),
})
export type VariantValues = z.infer<typeof variantSchema>

export const productDetailsSchema = z.object({
  name: z.string().trim().min(1, 'Enter a product name.').max(150),
  description: z.string().trim().max(2000).optional(),
  categoryId: z.string().optional(),
  unitOfMeasure: z.enum(UNITS),
})
export type ProductDetailsValues = z.infer<typeof productDetailsSchema>

export const createProductSchema = productDetailsSchema
  .extend({
    variants: z.array(variantSchema).min(1, 'Add at least one variant.').max(50),
  })
  .superRefine((value, ctx) => {
    // Same rule as the server: SKUs must differ within the product (case-insensitive).
    const seen = new Map<string, number>()
    value.variants.forEach((v, i) => {
      const key = v.sku.trim().toUpperCase()
      if (seen.has(key)) {
        ctx.addIssue({ code: 'custom', path: ['variants', i, 'sku'], message: 'This SKU is already used above.' })
      }
      seen.set(key, i)
    })
  })
export type CreateProductValues = z.infer<typeof createProductSchema>

export const categorySchema = z.object({
  name: z.string().trim().min(1, 'Enter a category name.').max(80),
  description: z.string().trim().max(300).optional(),
  parentId: z.string().optional(),
})
export type CategoryValues = z.infer<typeof categorySchema>

export const emptyVariant = (): VariantValues => ({
  sku: '',
  name: '',
  barcode: '',
  costPrice: Number.NaN,
  sellingPrice: Number.NaN,
  reorderLevel: 0,
})

/** "" from an empty input becomes undefined/null before sending, so the API stores NULL, not "". */
export const blankToNull = (value: string | undefined) => (value && value.trim() ? value.trim() : null)
