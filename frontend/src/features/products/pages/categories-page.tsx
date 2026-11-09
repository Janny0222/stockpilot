import { zodResolver } from '@hookform/resolvers/zod'
import { ArrowLeft, FolderTree, LoaderCircle, Pencil, Plus, Trash2 } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Controller, useForm } from 'react-hook-form'
import { Link } from 'react-router'
import { toast } from 'sonner'
import { ConfirmDialog } from '@/components/shared/confirm-dialog'
import { FormField } from '@/components/shared/form-field'
import { EmptyState, ErrorState, PageHeader } from '@/components/shared/states'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Skeleton } from '@/components/ui/skeleton'
import { FormAlert } from '@/features/auth/components/form-alert'
import { applyAllFieldErrors, toApiError } from '@/lib/api/errors'
import type { CategoryDto } from '@/lib/api/types'
import { Permissions as P } from '@/lib/permissions'
import { cn } from '@/lib/utils'
import { useHasPermission } from '@/stores/auth-store'
import { useCategories, useDeleteCategory, useSaveCategory } from '../api'
import { CategorySelect, sortCategoriesAsTree } from '../components/product-parts'
import { blankToNull, categorySchema, type CategoryValues } from '../schemas'

export function CategoriesPage() {
  const query = useCategories()
  const canManage = useHasPermission(P.Products.Manage)
  const remove = useDeleteCategory()
  const [dialog, setDialog] = useState<{ open: boolean; category?: CategoryDto }>({ open: false })

  const rows = sortCategoriesAsTree(query.data ?? [])
  const hasChildren = (id: string) => rows.some((c) => c.parentId === id)

  return (
    <>
      <PageHeader
        title="Categories"
        description="Group products the way your team looks for them. One level of subcategories."
        actions={
          <>
            <Button asChild variant="outline">
              <Link to="/products">
                <ArrowLeft /> Products
              </Link>
            </Button>
            {canManage && (
              <Button onClick={() => setDialog({ open: true })}>
                <Plus /> Add category
              </Button>
            )}
          </>
        }
      />

      <Card className="max-w-3xl gap-0 overflow-hidden py-0 shadow-card">
        {query.isPending ? (
          <div className="grid gap-3 p-6">
            <Skeleton className="h-8" />
            <Skeleton className="h-8" />
            <Skeleton className="h-8" />
          </div>
        ) : query.isError ? (
          <ErrorState error={query.error} onRetry={() => query.refetch()} />
        ) : rows.length === 0 ? (
          <EmptyState
            icon={FolderTree}
            title="No categories yet"
            description="Categories make the product list easier to filter, for example Electrical → Lighting."
            action={canManage && <Button onClick={() => setDialog({ open: true })}>Add a category</Button>}
          />
        ) : (
          <ul className="divide-y">
            {rows.map((c) => (
              <li key={c.id} className={cn('flex items-center gap-3 py-3 pr-4', c.depth ? 'pl-12' : 'pl-5')}>
                <div className="min-w-0 flex-1">
                  <p className={cn('truncate', c.depth ? 'font-medium' : 'font-semibold')}>{c.name}</p>
                  {c.description && <p className="truncate text-sm text-muted-foreground">{c.description}</p>}
                </div>
                <Link
                  to={`/products?category=${c.id}`}
                  className="shrink-0 rounded text-sm text-muted-foreground tabular hover:text-primary hover:underline"
                >
                  {c.productCount} {c.productCount === 1 ? 'product' : 'products'}
                </Link>
                {canManage && (
                  <div className="flex shrink-0">
                    <Button variant="ghost" size="icon-sm" aria-label={`Edit ${c.name}`} onClick={() => setDialog({ open: true, category: c })}>
                      <Pencil />
                    </Button>
                    <ConfirmDialog
                      trigger={
                        <Button
                          variant="ghost"
                          size="icon-sm"
                          aria-label={`Delete ${c.name}`}
                          className="text-destructive hover:text-destructive"
                          disabled={c.productCount > 0 || hasChildren(c.id)}
                          title={c.productCount > 0 || hasChildren(c.id) ? 'Only empty categories can be deleted' : undefined}
                        >
                          <Trash2 />
                        </Button>
                      }
                      title={`Delete ${c.name}?`}
                      description="This category has no products, so nothing else changes."
                      confirmLabel="Delete category"
                      destructive
                      onConfirm={() =>
                        remove.mutate(c.id, {
                          onSuccess: () => toast.success(`${c.name} deleted.`),
                          onError: (e) => toast.error(toApiError(e).message),
                        })
                      }
                    />
                  </div>
                )}
              </li>
            ))}
          </ul>
        )}
      </Card>

      {canManage && (
        <CategoryDialog
          open={dialog.open}
          category={dialog.category}
          hasChildren={dialog.category ? hasChildren(dialog.category.id) : false}
          onOpenChange={(open) => setDialog((d) => ({ ...d, open }))}
        />
      )}
    </>
  )
}

function CategoryDialog({
  open,
  category,
  hasChildren,
  onOpenChange,
}: {
  open: boolean
  category?: CategoryDto
  hasChildren: boolean
  onOpenChange: (open: boolean) => void
}) {
  const save = useSaveCategory()
  const form = useForm<CategoryValues>({ resolver: zodResolver(categorySchema) })

  useEffect(() => {
    if (open) form.reset({ name: category?.name ?? '', description: category?.description ?? '', parentId: category?.parentId ?? undefined })
  }, [open, category, form])

  const onSubmit = form.handleSubmit(async (values) => {
    try {
      await save.mutateAsync({
        id: category?.id,
        body: { name: values.name, description: blankToNull(values.description), parentId: values.parentId ?? null },
      })
      toast.success(category ? 'Category saved.' : `${values.name} added.`)
      onOpenChange(false)
    } catch (e) {
      const error = toApiError(e)
      if (error.code === 'CATEGORY_NAME_TAKEN') form.setError('name', { type: 'server', message: error.message })
      else if (!applyAllFieldErrors(error, form.setError)) form.setError('root', { message: error.message })
    }
  })

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{category ? `Edit ${category.name}` : 'Add category'}</DialogTitle>
          <DialogDescription>Choose a parent to make it a subcategory.</DialogDescription>
        </DialogHeader>
        <form id="category-form" noValidate onSubmit={onSubmit} className="grid gap-4">
          {form.formState.errors.root?.message && <FormAlert tone="error">{form.formState.errors.root.message}</FormAlert>}
          <FormField label="Name" registration={form.register('name')} error={form.formState.errors.name} />
          <FormField label="Description" registration={form.register('description')} error={form.formState.errors.description} placeholder="Optional" />
          {hasChildren ? (
            <p className="text-sm text-muted-foreground">This category has subcategories, so it stays at the top level.</p>
          ) : (
            <Controller
              control={form.control}
              name="parentId"
              render={({ field }) => (
                <CategorySelect
                  id="parent"
                  label="Parent category"
                  value={field.value}
                  onChange={field.onChange}
                  placeholder="None (top level)"
                  onlyTopLevel
                  excludeId={category?.id}
                />
              )}
            />
          )}
        </form>
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
          <Button type="submit" form="category-form" disabled={save.isPending}>
            {save.isPending && <LoaderCircle className="animate-spin" aria-hidden="true" />}
            {category ? 'Save category' : 'Add category'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
