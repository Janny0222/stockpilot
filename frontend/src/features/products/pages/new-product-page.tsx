import { zodResolver } from '@hookform/resolvers/zod'
import { LoaderCircle, Plus, Trash2 } from 'lucide-react'
import { Controller, useFieldArray, useForm } from 'react-hook-form'
import { Link, useNavigate } from 'react-router'
import { toast } from 'sonner'
import { FormField } from '@/components/shared/form-field'
import { PageHeader } from '@/components/shared/states'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Textarea } from '@/components/ui/textarea'
import { FormAlert } from '@/features/auth/components/form-alert'
import { applyAllFieldErrors, toApiError } from '@/lib/api/errors'
import { Permissions as P } from '@/lib/permissions'
import { useHasPermission } from '@/stores/auth-store'
import { useCreateProduct } from '../api'
import { CategorySelect, VariantFields } from '../components/product-parts'
import { blankToNull, createProductSchema, emptyVariant, UNITS, type CreateProductValues } from '../schemas'

export function NewProductPage() {
  const navigate = useNavigate()
  const canViewCost = useHasPermission(P.Inventory.ViewCost)
  const create = useCreateProduct()

  const form = useForm<CreateProductValues>({
    resolver: zodResolver(createProductSchema),
    defaultValues: { name: '', description: '', categoryId: undefined, unitOfMeasure: 'pcs', variants: [emptyVariant()] },
  })
  const variants = useFieldArray({ control: form.control, name: 'variants' })
  const { errors } = form.formState

  const onSubmit = form.handleSubmit(async (values) => {
    try {
      const product = await create.mutateAsync({
        name: values.name,
        description: blankToNull(values.description),
        categoryId: values.categoryId ?? null,
        unitOfMeasure: values.unitOfMeasure,
        variants: values.variants.map((v) => ({
          sku: v.sku,
          name: blankToNull(v.name),
          barcode: blankToNull(v.barcode),
          costPrice: canViewCost ? v.costPrice : 0,
          sellingPrice: v.sellingPrice,
          reorderLevel: v.reorderLevel,
        })),
      })
      toast.success(`${product.name} added.`)
      navigate(`/products/${product.id}`, { replace: true })
    } catch (e) {
      const error = toApiError(e)
      if (error.code === 'SKU_TAKEN' || error.code === 'BARCODE_TAKEN') {
        // Point at the row holding the clashing code: the message quotes it, e.g. SKU 'LED-9W'.
        const code = /'([^']+)'/.exec(error.message)?.[1]?.toUpperCase()
        const field = error.code === 'SKU_TAKEN' ? 'sku' : 'barcode'
        const index = values.variants.findIndex((v) => (v[field] ?? '').trim().toUpperCase() === code)
        if (index >= 0) {
          form.setError(`variants.${index}.${field}`, { type: 'server', message: error.message })
          return
        }
      }
      if (!applyAllFieldErrors(error, form.setError)) {
        form.setError('root', { message: error.message })
      }
    }
  })

  return (
    <>
      <PageHeader title="Add product" description="Describe the product, then add one variant per SKU you stock." />

      <form noValidate onSubmit={onSubmit} className="grid max-w-4xl gap-6">
        {errors.root?.message && <FormAlert tone="error">{errors.root.message}</FormAlert>}

        <Card className="shadow-card">
          <CardHeader>
            <CardTitle>Details</CardTitle>
          </CardHeader>
          <CardContent className="grid gap-5">
            <FormField label="Product name" autoFocus registration={form.register('name')} error={errors.name} placeholder="e.g. LED Bulb E27 9W Daylight" />
            <div className="grid gap-5 sm:grid-cols-2">
              <Controller
                control={form.control}
                name="categoryId"
                render={({ field }) => <CategorySelect id="category" label="Category" value={field.value} onChange={field.onChange} />}
              />
              <div className="grid gap-1.5">
                <Label htmlFor="unit">Unit</Label>
                <Controller
                  control={form.control}
                  name="unitOfMeasure"
                  render={({ field }) => (
                    <Select value={field.value} onValueChange={field.onChange}>
                      <SelectTrigger id="unit" className="h-10 w-full bg-card">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        {UNITS.map((u) => (
                          <SelectItem key={u} value={u}>
                            {u}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                  )}
                />
              </div>
            </div>
            <div className="grid gap-1.5">
              <Label htmlFor="description">Description</Label>
              <Textarea id="description" rows={3} className="bg-card" placeholder="Optional" {...form.register('description')} />
            </div>
          </CardContent>
        </Card>

        <Card className="shadow-card">
          <CardHeader>
            <CardTitle>Variants</CardTitle>
            <CardDescription>
              Each variant is stocked and sold separately: sizes, colours or pack sizes. A simple product has just one.
            </CardDescription>
          </CardHeader>
          <CardContent className="grid gap-4">
            {errors.variants?.root?.message && <p className="text-sm text-destructive">{errors.variants.root.message}</p>}
            {variants.fields.map((field, index) => (
              <fieldset key={field.id} className="rounded-xl border p-4">
                <legend className="px-1 text-sm font-semibold">Variant {index + 1}</legend>
                <VariantFields
                  register={form.register}
                  errors={errors.variants?.[index]}
                  prefix={`variants.${index}.`}
                  showCost={canViewCost}
                  compact
                />
                {variants.fields.length > 1 && (
                  <Button type="button" variant="ghost" size="sm" className="mt-3 text-destructive hover:text-destructive" onClick={() => variants.remove(index)}>
                    <Trash2 /> Remove variant
                  </Button>
                )}
              </fieldset>
            ))}
            <Button type="button" variant="outline" className="justify-self-start" onClick={() => variants.append(emptyVariant())} disabled={variants.fields.length >= 50}>
              <Plus /> Add another variant
            </Button>
          </CardContent>
        </Card>

        <div className="flex flex-wrap gap-3">
          <Button type="submit" size="lg" disabled={create.isPending}>
            {create.isPending && <LoaderCircle className="animate-spin" aria-hidden="true" />}
            Save product
          </Button>
          <Button asChild type="button" variant="outline" size="lg">
            <Link to="/products">Cancel</Link>
          </Button>
        </div>
      </form>
    </>
  )
}
