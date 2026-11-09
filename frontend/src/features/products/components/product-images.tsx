import { ImagePlus, LoaderCircle, Star, Trash2 } from 'lucide-react'
import { useRef, useState } from 'react'
import { toast } from 'sonner'
import { ConfirmDialog } from '@/components/shared/confirm-dialog'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { toApiError } from '@/lib/api/errors'
import type { ProductDetailDto } from '@/lib/api/types'
import { cn } from '@/lib/utils'
import { useDeleteProductImage, useSetPrimaryImage, useUploadProductImage } from '../api'
import { ProductImage } from './product-parts'

const MAX_IMAGES = 5
const MAX_BYTES = 2 * 1024 * 1024
const ACCEPTED = ['image/jpeg', 'image/png', 'image/webp']

export function ProductImagesCard({ product, canManage }: { product: ProductDetailDto; canManage: boolean }) {
  const images = product.images
  const primary = images.find((i) => i.isPrimary) ?? images[0]
  const [selectedId, setSelectedId] = useState<string>()
  const selected = images.find((i) => i.id === selectedId) ?? primary
  const fileInput = useRef<HTMLInputElement>(null)

  const upload = useUploadProductImage(product.id)
  const remove = useDeleteProductImage(product.id)
  const makePrimary = useSetPrimaryImage(product.id)

  const onFile = async (file: File | undefined) => {
    if (!file) return
    // Quick client-side checks for a fast answer; the server re-checks the actual file content.
    if (!ACCEPTED.includes(file.type)) return toast.error('Upload a JPEG, PNG or WebP image.')
    if (file.size > MAX_BYTES) return toast.error('Images must be 2 MB or smaller.')
    try {
      const image = await upload.mutateAsync(file)
      setSelectedId(image.id)
      toast.success('Image uploaded.')
    } catch (e) {
      toast.error(toApiError(e).message)
    } finally {
      if (fileInput.current) fileInput.current.value = ''
    }
  }

  return (
    <Card className="shadow-card">
      <CardHeader className="flex flex-row items-center justify-between gap-2">
        <CardTitle>Images</CardTitle>
        <span className="text-sm text-muted-foreground tabular">
          {images.length} / {MAX_IMAGES}
        </span>
      </CardHeader>
      <CardContent className="grid gap-3">
        <ProductImage productId={product.id} imageId={selected?.id} alt={product.name} className="aspect-square w-full rounded-xl" />

        {images.length > 0 && (
          <ul className="grid grid-cols-5 gap-2" aria-label="Product images">
            {images.map((image) => (
              <li key={image.id}>
                <button
                  type="button"
                  onClick={() => setSelectedId(image.id)}
                  aria-label={`Show image${image.isPrimary ? ' (main)' : ''}`}
                  aria-pressed={selected?.id === image.id}
                  className={cn(
                    'relative block w-full rounded-lg focus-visible:outline-2 focus-visible:outline-ring',
                    selected?.id === image.id && 'ring-2 ring-primary ring-offset-2',
                  )}
                >
                  <ProductImage productId={product.id} imageId={image.id} alt="" className="aspect-square w-full" />
                  {image.isPrimary && (
                    <Star className="absolute top-1 right-1 size-3.5 fill-warning text-warning drop-shadow" aria-hidden="true" />
                  )}
                </button>
              </li>
            ))}
          </ul>
        )}

        {canManage && (
          <div className="flex flex-wrap gap-2">
            <input
              ref={fileInput}
              type="file"
              accept={ACCEPTED.join(',')}
              className="sr-only"
              id="product-image-upload"
              onChange={(e) => onFile(e.target.files?.[0])}
            />
            <Button
              variant="outline"
              size="sm"
              onClick={() => fileInput.current?.click()}
              disabled={upload.isPending || images.length >= MAX_IMAGES || product.status === 'Archived'}
            >
              {upload.isPending ? <LoaderCircle className="animate-spin" /> : <ImagePlus />}
              {images.length >= MAX_IMAGES ? 'Image limit reached' : 'Upload image'}
            </Button>
            {selected && !selected.isPrimary && (
              <Button variant="ghost" size="sm" onClick={() => makePrimary.mutate(selected.id)} disabled={makePrimary.isPending}>
                <Star /> Make main image
              </Button>
            )}
            {selected && (
              <ConfirmDialog
                trigger={
                  <Button variant="ghost" size="sm" className="text-destructive hover:text-destructive" disabled={remove.isPending}>
                    <Trash2 /> Delete
                  </Button>
                }
                title="Delete this image?"
                description="The image is removed permanently. You can upload it again later."
                confirmLabel="Delete image"
                destructive
                onConfirm={() =>
                  remove.mutate(selected.id, {
                    onSuccess: () => {
                      setSelectedId(undefined)
                      toast.success('Image deleted.')
                    },
                    onError: (e) => toast.error(toApiError(e).message),
                  })
                }
              />
            )}
          </div>
        )}
        <p className="text-xs text-muted-foreground">JPEG, PNG or WebP up to 2 MB. Photos are resized and their location data removed.</p>
      </CardContent>
    </Card>
  )
}
