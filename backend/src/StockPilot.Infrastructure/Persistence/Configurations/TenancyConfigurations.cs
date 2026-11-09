using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockPilot.Domain.Auditing;
using StockPilot.Domain.Tenancy;
using StockPilot.Domain.Warehousing;

namespace StockPilot.Infrastructure.Persistence.Configurations;

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.Property(t => t.Name).HasMaxLength(Tenant.NameMaxLength).IsRequired();
        builder.Property(t => t.Slug).HasMaxLength(Domain.Common.Slug.MaxLength + 40).IsRequired();
        builder.HasIndex(t => t.Slug).IsUnique();
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.Currency).HasMaxLength(3).IsFixedLength();
        builder.Property(t => t.TimeZone).HasMaxLength(64);
    }
}

internal sealed class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        builder.Property(w => w.Code).HasMaxLength(Warehouse.CodeMaxLength).IsRequired();
        builder.Property(w => w.Name).HasMaxLength(Warehouse.NameMaxLength).IsRequired();
        builder.Property(w => w.AddressLine).HasMaxLength(200);
        builder.Property(w => w.City).HasMaxLength(100);
        builder.Property(w => w.Country).HasMaxLength(100);

        // Codes are unique *within* a company. Two companies may both have "MAIN".
        builder.HasIndex(w => new { w.TenantId, w.Code }).IsUnique();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(w => w.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.Property(a => a.Action).HasMaxLength(100).IsRequired();
        builder.Property(a => a.EntityType).HasMaxLength(100).IsRequired();
        builder.Property(a => a.EntityId).HasMaxLength(128); // composite keys are "guid,guid"
        builder.Property(a => a.Changes).HasColumnType("jsonb");
        builder.Property(a => a.IpAddress).HasMaxLength(64);
        builder.Property(a => a.CorrelationId).HasMaxLength(64);

        // The audit viewer lists a tenant's events newest-first.
        builder.HasIndex(a => new { a.TenantId, a.OccurredAt });
    }
}
