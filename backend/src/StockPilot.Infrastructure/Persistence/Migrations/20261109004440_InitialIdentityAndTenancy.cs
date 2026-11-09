using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace StockPilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialIdentityAndTenancy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    changes = table.Column<string>(type: "jsonb", nullable: true),
                    ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "permissions",
                columns: table => new
                {
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    group = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permissions", x => x.code);
                });

            migrationBuilder.CreateTable(
                name: "tenants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    time_zone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    allow_negative_stock = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_system = table.Column<bool>(type: "boolean", nullable: false),
                    restrict_to_assigned_warehouses = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                    table.ForeignKey(
                        name: "fk_roles_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "warehouses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    address_line = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_warehouses", x => x.id);
                    table.ForeignKey(
                        name: "fk_warehouses_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "role_permissions",
                columns: table => new
                {
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_permissions", x => new { x.role_id, x.permission_code });
                    table.ForeignKey(
                        name: "fk_role_permissions_permissions_permission_code",
                        column: x => x.permission_code,
                        principalTable: "permissions",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_role_permissions_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    security_stamp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.ForeignKey(
                        name: "fk_users_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_users_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    family_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoke_reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    replaced_by_token_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_refresh_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_warehouse_access",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_warehouse_access", x => new { x.user_id, x.warehouse_id });
                    table.ForeignKey(
                        name: "fk_user_warehouse_access_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_warehouse_access_warehouses_warehouse_id",
                        column: x => x.warehouse_id,
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "code", "description", "group" },
                values: new object[,]
                {
                    { "audit.view", "View the audit log", "Settings" },
                    { "company.manage", "Manage company profile and policies", "Settings" },
                    { "customers.manage", "Create and edit customers", "Customers" },
                    { "customers.view", "View customers", "Customers" },
                    { "dashboard.view", "View the dashboard", "Dashboard" },
                    { "inventory.adjust", "Record stock adjustments", "Inventory" },
                    { "inventory.stock_in", "Record stock-in movements", "Inventory" },
                    { "inventory.stock_out", "Record stock-out movements", "Inventory" },
                    { "inventory.transfer", "Transfer stock between warehouses", "Inventory" },
                    { "inventory.view", "View stock levels and movement history", "Inventory" },
                    { "inventory.view_cost", "View cost prices and inventory valuation", "Inventory" },
                    { "platform.metrics.view", "View platform metrics", "Platform" },
                    { "platform.tenants.manage", "Suspend and activate companies", "Platform" },
                    { "platform.tenants.view", "View registered companies", "Platform" },
                    { "products.manage", "Create, edit and archive products, variants, categories and images", "Products" },
                    { "products.view", "View products and categories", "Products" },
                    { "purchases.approve", "Approve purchase orders", "Purchases" },
                    { "purchases.manage", "Create, edit, submit and cancel purchase orders", "Purchases" },
                    { "purchases.receive", "Receive purchased items into stock", "Purchases" },
                    { "purchases.view", "View purchase orders", "Purchases" },
                    { "reports.export", "Export reports to CSV", "Reports" },
                    { "reports.view", "View reports", "Reports" },
                    { "roles.manage", "Manage roles and permissions", "Settings" },
                    { "sales.cancel", "Cancel sales orders", "Sales" },
                    { "sales.create", "Create, edit and confirm sales orders", "Sales" },
                    { "sales.fulfil", "Fulfil (ship) sales orders", "Sales" },
                    { "sales.view", "View sales orders", "Sales" },
                    { "suppliers.manage", "Create, edit and archive suppliers", "Suppliers" },
                    { "suppliers.view", "View suppliers", "Suppliers" },
                    { "users.manage", "Invite, edit and deactivate users", "Settings" },
                    { "warehouses.manage", "Create, edit and archive warehouses", "Warehouses" },
                    { "warehouses.view", "View warehouses", "Warehouses" }
                });

            migrationBuilder.InsertData(
                table: "roles",
                columns: new[] { "id", "is_system", "name", "restrict_to_assigned_warehouses", "tenant_id" },
                values: new object[,]
                {
                    { new Guid("01900000-0000-7000-8000-000000000001"), true, "Super Admin", false, null },
                    { new Guid("01900000-0000-7000-8000-000000000002"), true, "Owner", false, null },
                    { new Guid("01900000-0000-7000-8000-000000000003"), true, "Manager", false, null },
                    { new Guid("01900000-0000-7000-8000-000000000004"), true, "Inventory Staff", true, null },
                    { new Guid("01900000-0000-7000-8000-000000000005"), true, "Sales Staff", false, null }
                });

            migrationBuilder.InsertData(
                table: "role_permissions",
                columns: new[] { "permission_code", "role_id" },
                values: new object[,]
                {
                    { "platform.metrics.view", new Guid("01900000-0000-7000-8000-000000000001") },
                    { "platform.tenants.manage", new Guid("01900000-0000-7000-8000-000000000001") },
                    { "platform.tenants.view", new Guid("01900000-0000-7000-8000-000000000001") },
                    { "audit.view", new Guid("01900000-0000-7000-8000-000000000002") },
                    { "company.manage", new Guid("01900000-0000-7000-8000-000000000002") },
                    { "customers.manage", new Guid("01900000-0000-7000-8000-000000000002") },
                    { "customers.view", new Guid("01900000-0000-7000-8000-000000000002") },
                    { "dashboard.view", new Guid("01900000-0000-7000-8000-000000000002") },
                    { "inventory.adjust", new Guid("01900000-0000-7000-8000-000000000002") },
                    { "inventory.stock_in", new Guid("01900000-0000-7000-8000-000000000002") },
                    { "inventory.stock_out", new Guid("01900000-0000-7000-8000-000000000002") },
                    { "inventory.transfer", new Guid("01900000-0000-7000-8000-000000000002") },
                    { "inventory.view", new Guid("01900000-0000-7000-8000-000000000002") },
                    { "inventory.view_cost", new Guid("01900000-0000-7000-8000-000000000002") },
                    { "products.manage", new Guid("01900000-0000-7000-8000-000000000002") },
                    { "products.view", new Guid("01900000-0000-7000-8000-000000000002") },
                    { "purchases.approve", new Guid("01900000-0000-7000-8000-000000000002") },
                    { "purchases.manage", new Guid("01900000-0000-7000-8000-000000000002") },
                    { "purchases.receive", new Guid("01900000-0000-7000-8000-000000000002") },
                    { "purchases.view", new Guid("01900000-0000-7000-8000-000000000002") },
                    { "reports.export", new Guid("01900000-0000-7000-8000-000000000002") },
                    { "reports.view", new Guid("01900000-0000-7000-8000-000000000002") },
                    { "roles.manage", new Guid("01900000-0000-7000-8000-000000000002") },
                    { "sales.cancel", new Guid("01900000-0000-7000-8000-000000000002") },
                    { "sales.create", new Guid("01900000-0000-7000-8000-000000000002") },
                    { "sales.fulfil", new Guid("01900000-0000-7000-8000-000000000002") },
                    { "sales.view", new Guid("01900000-0000-7000-8000-000000000002") },
                    { "suppliers.manage", new Guid("01900000-0000-7000-8000-000000000002") },
                    { "suppliers.view", new Guid("01900000-0000-7000-8000-000000000002") },
                    { "users.manage", new Guid("01900000-0000-7000-8000-000000000002") },
                    { "warehouses.manage", new Guid("01900000-0000-7000-8000-000000000002") },
                    { "warehouses.view", new Guid("01900000-0000-7000-8000-000000000002") },
                    { "customers.manage", new Guid("01900000-0000-7000-8000-000000000003") },
                    { "customers.view", new Guid("01900000-0000-7000-8000-000000000003") },
                    { "dashboard.view", new Guid("01900000-0000-7000-8000-000000000003") },
                    { "inventory.adjust", new Guid("01900000-0000-7000-8000-000000000003") },
                    { "inventory.stock_in", new Guid("01900000-0000-7000-8000-000000000003") },
                    { "inventory.stock_out", new Guid("01900000-0000-7000-8000-000000000003") },
                    { "inventory.transfer", new Guid("01900000-0000-7000-8000-000000000003") },
                    { "inventory.view", new Guid("01900000-0000-7000-8000-000000000003") },
                    { "inventory.view_cost", new Guid("01900000-0000-7000-8000-000000000003") },
                    { "products.manage", new Guid("01900000-0000-7000-8000-000000000003") },
                    { "products.view", new Guid("01900000-0000-7000-8000-000000000003") },
                    { "purchases.approve", new Guid("01900000-0000-7000-8000-000000000003") },
                    { "purchases.manage", new Guid("01900000-0000-7000-8000-000000000003") },
                    { "purchases.receive", new Guid("01900000-0000-7000-8000-000000000003") },
                    { "purchases.view", new Guid("01900000-0000-7000-8000-000000000003") },
                    { "reports.export", new Guid("01900000-0000-7000-8000-000000000003") },
                    { "reports.view", new Guid("01900000-0000-7000-8000-000000000003") },
                    { "sales.cancel", new Guid("01900000-0000-7000-8000-000000000003") },
                    { "sales.create", new Guid("01900000-0000-7000-8000-000000000003") },
                    { "sales.fulfil", new Guid("01900000-0000-7000-8000-000000000003") },
                    { "sales.view", new Guid("01900000-0000-7000-8000-000000000003") },
                    { "suppliers.manage", new Guid("01900000-0000-7000-8000-000000000003") },
                    { "suppliers.view", new Guid("01900000-0000-7000-8000-000000000003") },
                    { "warehouses.manage", new Guid("01900000-0000-7000-8000-000000000003") },
                    { "warehouses.view", new Guid("01900000-0000-7000-8000-000000000003") },
                    { "dashboard.view", new Guid("01900000-0000-7000-8000-000000000004") },
                    { "inventory.stock_in", new Guid("01900000-0000-7000-8000-000000000004") },
                    { "inventory.stock_out", new Guid("01900000-0000-7000-8000-000000000004") },
                    { "inventory.transfer", new Guid("01900000-0000-7000-8000-000000000004") },
                    { "inventory.view", new Guid("01900000-0000-7000-8000-000000000004") },
                    { "products.view", new Guid("01900000-0000-7000-8000-000000000004") },
                    { "purchases.receive", new Guid("01900000-0000-7000-8000-000000000004") },
                    { "purchases.view", new Guid("01900000-0000-7000-8000-000000000004") },
                    { "sales.fulfil", new Guid("01900000-0000-7000-8000-000000000004") },
                    { "sales.view", new Guid("01900000-0000-7000-8000-000000000004") },
                    { "suppliers.view", new Guid("01900000-0000-7000-8000-000000000004") },
                    { "warehouses.view", new Guid("01900000-0000-7000-8000-000000000004") },
                    { "customers.manage", new Guid("01900000-0000-7000-8000-000000000005") },
                    { "customers.view", new Guid("01900000-0000-7000-8000-000000000005") },
                    { "dashboard.view", new Guid("01900000-0000-7000-8000-000000000005") },
                    { "inventory.view", new Guid("01900000-0000-7000-8000-000000000005") },
                    { "products.view", new Guid("01900000-0000-7000-8000-000000000005") },
                    { "sales.create", new Guid("01900000-0000-7000-8000-000000000005") },
                    { "sales.fulfil", new Guid("01900000-0000-7000-8000-000000000005") },
                    { "sales.view", new Guid("01900000-0000-7000-8000-000000000005") },
                    { "warehouses.view", new Guid("01900000-0000-7000-8000-000000000005") }
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_tenant_id_occurred_at",
                table: "audit_logs",
                columns: new[] { "tenant_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_family_id",
                table: "refresh_tokens",
                column: "family_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_user_id_revoked_at",
                table: "refresh_tokens",
                columns: new[] { "user_id", "revoked_at" });

            migrationBuilder.CreateIndex(
                name: "ix_role_permissions_permission_code",
                table: "role_permissions",
                column: "permission_code");

            migrationBuilder.CreateIndex(
                name: "ix_roles_tenant_id",
                table: "roles",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenants_slug",
                table: "tenants",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_tokens_token_hash",
                table: "user_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_tokens_user_id",
                table: "user_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_warehouse_access_tenant_id",
                table: "user_warehouse_access",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_warehouse_access_warehouse_id",
                table: "user_warehouse_access",
                column: "warehouse_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_role_id",
                table: "users",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_tenant_id",
                table: "users",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_warehouses_tenant_id_code",
                table: "warehouses",
                columns: new[] { "tenant_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "role_permissions");

            migrationBuilder.DropTable(
                name: "user_tokens");

            migrationBuilder.DropTable(
                name: "user_warehouse_access");

            migrationBuilder.DropTable(
                name: "permissions");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "warehouses");

            migrationBuilder.DropTable(
                name: "roles");

            migrationBuilder.DropTable(
                name: "tenants");
        }
    }
}
