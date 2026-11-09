import type { components } from '@/types/api.gen'

/**
 * Friendly aliases for the generated OpenAPI schemas. `api.gen.ts` is regenerated from the backend
 * (`npm run gen:api`), so if the C# DTO changes, TypeScript flags every place that breaks.
 */
type Schemas = components['schemas']

export type MeResponse = Schemas['MeResponse']
export type AccessTokenResponse = Schemas['AccessTokenResponse']
export type RegisterCompanyCommand = Schemas['RegisterCompanyCommand']
export type RegisterCompanyResult = Schemas['RegisterCompanyResult']
export type SessionDto = Schemas['SessionDto']
export type WarehouseDto = Schemas['WarehouseDto']

export type ProductStatus = Schemas['ProductStatus']
export type ProductListItemDto = Schemas['ProductListItemDto']
export type ProductDetailDto = Schemas['ProductDetailDto']
export type VariantDto = Schemas['VariantDto']
export type ProductImageDto = Schemas['ProductImageDto']
export type CategoryDto = Schemas['CategoryDto']
export type CreateProductCommand = Schemas['CreateProductCommand']
export type UpdateProductCommand = Schemas['UpdateProductCommand']
export type VariantInput = Schemas['VariantInput']
export type UpdateVariantCommand = Schemas['UpdateVariantCommand']
export type SaveCategoryCommand = Schemas['SaveCategoryCommand']
export type PagedResult<T> = {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
  totalPages?: number
}

/** Query-string parameters shared by every paged list endpoint. */
export type PageParams = {
  page?: number
  pageSize?: number
  search?: string
  sort?: string
}
