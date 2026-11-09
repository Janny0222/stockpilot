import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/lib/api/client'
import type {
  CategoryDto,
  CreateProductCommand,
  PageParams,
  PagedResult,
  ProductDetailDto,
  ProductImageDto,
  ProductListItemDto,
  ProductStatus,
  SaveCategoryCommand,
  UpdateProductCommand,
  UpdateVariantCommand,
  VariantInput,
} from '@/lib/api/types'

export type ProductListParams = PageParams & { categoryId?: string; status?: ProductStatus }

export const productKeys = {
  all: ['products'] as const,
  list: (params: ProductListParams) => [...productKeys.all, 'list', params] as const,
  detail: (id: string) => [...productKeys.all, 'detail', id] as const,
  image: (productId: string, imageId: string) => [...productKeys.all, 'image', productId, imageId] as const,
}

export const categoryKeys = { all: ['categories'] as const }

// ---------------- Queries ----------------

export function useProducts(params: ProductListParams) {
  return useQuery({
    queryKey: productKeys.list(params),
    queryFn: ({ signal }) => api.get<PagedResult<ProductListItemDto>>('/products', { params, signal }).then((r) => r.data),
    placeholderData: (previous) => previous,
  })
}

export function useProduct(id: string) {
  return useQuery({
    queryKey: productKeys.detail(id),
    queryFn: ({ signal }) => api.get<ProductDetailDto>(`/products/${id}`, { signal }).then((r) => r.data),
  })
}

export function useCategories() {
  return useQuery({
    queryKey: categoryKeys.all,
    queryFn: ({ signal }) => api.get<CategoryDto[]>('/categories', { signal }).then((r) => r.data),
    staleTime: 5 * 60_000, // categories change rarely
  })
}

/**
 * Product images need the bearer token, which an <img src> can't send. So we fetch the bytes with
 * axios and cache a blob: URL. Image IDs never change content, so it never goes stale. The URL is
 * released (URL.revokeObjectURL) when the query cache drops it, see createQueryClient().
 */
export function useProductImage(productId: string, imageId: string | null | undefined) {
  return useQuery({
    queryKey: productKeys.image(productId, imageId ?? ''),
    queryFn: ({ signal }) =>
      api
        .get<Blob>(`/products/${productId}/images/${imageId}`, { responseType: 'blob', signal })
        .then((r) => URL.createObjectURL(r.data)),
    enabled: !!imageId,
    staleTime: Infinity,
    gcTime: 10 * 60_000,
  })
}

// ---------------- Mutations ----------------
// Every successful write invalidates ['products'], so lists and details refetch fresh data.

function useInvalidateProducts() {
  const queryClient = useQueryClient()
  return () => queryClient.invalidateQueries({ queryKey: productKeys.all })
}

export function useCreateProduct() {
  const invalidate = useInvalidateProducts()
  return useMutation({
    mutationFn: (body: CreateProductCommand) => api.post<ProductDetailDto>('/products', body).then((r) => r.data),
    onSuccess: invalidate,
  })
}

export function useUpdateProduct(id: string) {
  const invalidate = useInvalidateProducts()
  return useMutation({
    mutationFn: (body: UpdateProductCommand) => api.put(`/products/${id}`, body),
    onSuccess: invalidate,
  })
}

export function useSetProductArchived(id: string) {
  const invalidate = useInvalidateProducts()
  return useMutation({
    mutationFn: (archive: boolean) => api.post(`/products/${id}/${archive ? 'archive' : 'restore'}`),
    onSuccess: invalidate,
  })
}

export function useAddVariant(productId: string) {
  const invalidate = useInvalidateProducts()
  return useMutation({
    mutationFn: (body: VariantInput) => api.post(`/products/${productId}/variants`, body),
    onSuccess: invalidate,
  })
}

export function useUpdateVariant(productId: string) {
  const invalidate = useInvalidateProducts()
  return useMutation({
    mutationFn: ({ variantId, body }: { variantId: string; body: UpdateVariantCommand }) =>
      api.put(`/products/${productId}/variants/${variantId}`, body),
    onSuccess: invalidate,
  })
}

export function useSetVariantArchived(productId: string) {
  const invalidate = useInvalidateProducts()
  return useMutation({
    mutationFn: ({ variantId, archive }: { variantId: string; archive: boolean }) =>
      api.post(`/products/${productId}/variants/${variantId}/${archive ? 'archive' : 'restore'}`),
    onSuccess: invalidate,
  })
}

export function useUploadProductImage(productId: string) {
  const invalidate = useInvalidateProducts()
  return useMutation({
    mutationFn: (file: File) => {
      const form = new FormData()
      form.append('file', file)
      return api.post<ProductImageDto>(`/products/${productId}/images`, form).then((r) => r.data)
    },
    onSuccess: invalidate,
  })
}

export function useDeleteProductImage(productId: string) {
  const invalidate = useInvalidateProducts()
  return useMutation({
    mutationFn: (imageId: string) => api.delete(`/products/${productId}/images/${imageId}`),
    onSuccess: invalidate,
  })
}

export function useSetPrimaryImage(productId: string) {
  const invalidate = useInvalidateProducts()
  return useMutation({
    mutationFn: (imageId: string) => api.post(`/products/${productId}/images/${imageId}/primary`),
    onSuccess: invalidate,
  })
}

function useInvalidateCategories() {
  const queryClient = useQueryClient()
  return () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: categoryKeys.all }),
      queryClient.invalidateQueries({ queryKey: productKeys.all }),
    ])
}

export function useSaveCategory() {
  const invalidate = useInvalidateCategories()
  return useMutation({
    mutationFn: ({ id, body }: { id?: string; body: SaveCategoryCommand }) =>
      (id ? api.put<CategoryDto>(`/categories/${id}`, body) : api.post<CategoryDto>('/categories', body)).then((r) => r.data),
    onSuccess: invalidate,
  })
}

export function useDeleteCategory() {
  const invalidate = useInvalidateCategories()
  return useMutation({
    mutationFn: (id: string) => api.delete(`/categories/${id}`),
    onSuccess: invalidate,
  })
}
