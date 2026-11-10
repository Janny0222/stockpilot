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

export type StockLevelDto = Schemas['StockLevelDto']
export type StockStatus = Schemas['StockStatus']
export type MovementDto = Schemas['MovementDto']
export type InventoryMovementType = Schemas['InventoryMovementType']
export type AdjustmentReason = Schemas['AdjustmentReason']
export type VariantOptionDto = Schemas['VariantOptionDto']
export type TransferListItemDto = Schemas['TransferListItemDto']
export type TransferDetailDto = Schemas['TransferDetailDto']
export type StockOperationResult = Schemas['StockOperationResult']
export type RecordStockCommand = Schemas['RecordStockCommand']
export type AdjustStockCommand = Schemas['AdjustStockCommand']
export type TransferStockCommand = Schemas['TransferStockCommand']
export type SaveWarehouseCommand = Schemas['SaveWarehouseCommand']

export type SupplierDto = Schemas['SupplierDto']
export type SaveSupplierCommand = Schemas['SaveSupplierCommand']
export type PurchaseOrderStatus = Schemas['PurchaseOrderStatus']
export type PurchaseOrderListItemDto = Schemas['PurchaseOrderListItemDto']
export type PurchaseOrderDetailDto = Schemas['PurchaseOrderDetailDto']
export type PurchaseOrderItemDto = Schemas['PurchaseOrderItemDto']
export type SavePurchaseOrderCommand = Schemas['SavePurchaseOrderCommand']
export type ReceivePurchaseOrderCommand = Schemas['ReceivePurchaseOrderCommand']
export type ReceiptResult = Schemas['ReceiptResult']
export type CustomerDto = Schemas['CustomerDto']
export type SaveCustomerCommand = Schemas['SaveCustomerCommand']
export type SalesOrderStatus = Schemas['SalesOrderStatus']
export type SalesOrderListItemDto = Schemas['SalesOrderListItemDto']
export type SalesOrderDetailDto = Schemas['SalesOrderDetailDto']
export type SalesOrderItemDto = Schemas['SalesOrderItemDto']
export type SaveSalesOrderCommand = Schemas['SaveSalesOrderCommand']
export type ShipSalesOrderCommand = Schemas['ShipSalesOrderCommand']
export type ShipmentResult = Schemas['ShipmentResult']
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
export type DashboardDto = Schemas['DashboardDto']
export type SalesTrendPointDto = Schemas['SalesTrendPointDto']
export type TopProductDto = Schemas['TopProductDto']
export type WarehouseStockDto = Schemas['WarehouseStockDto']
export type ReportTableDto = Schemas['ReportTableDto']
export type ReportColumnDto = Schemas['ReportColumnDto']
export type ReportColumnKind = Schemas['ReportColumnKind']
export type CompanySettingsDto = Schemas['CompanySettingsDto']
export type UpdateCompanySettingsCommand = Schemas['UpdateCompanySettingsCommand']
export type UserDto = Schemas['UserDto']
export type UserStatus = Schemas['UserStatus']
export type InviteUserCommand = Schemas['InviteUserCommand']
export type UpdateUserCommand = Schemas['UpdateUserCommand']
export type RolesOverviewDto = Schemas['RolesOverviewDto']
export type RoleOptionDto = Schemas['RoleOptionDto']
export type AuditLogDto = Schemas['AuditLogDto']
export type AuditFiltersDto = Schemas['AuditFiltersDto']
export type InvitationDto = Schemas['InvitationDto']
