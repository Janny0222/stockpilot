using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockPilot.Domain.Identity;
using StockPilot.Domain.Notifications;
using StockPilot.Domain.Tenancy;

namespace StockPilot.Infrastructure.Persistence.Configurations;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.Property(n => n.Type).HasConversion<string>().HasMaxLength(50);
        builder.Property(n => n.Title).HasMaxLength(Notification.TitleMaxLength).IsRequired();
        builder.Property(n => n.Message).HasMaxLength(Notification.MessageMaxLength).IsRequired();
        builder.Property(n => n.EntityType).HasMaxLength(50);

        // The bell polls "how many unread?" and the list reads newest-first, both per person.
        builder.HasIndex(n => new { n.RecipientUserId, n.ReadAt, n.CreatedAt });
        builder.HasIndex(n => n.TenantId);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(n => n.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(n => n.RecipientUserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        builder.HasKey(p => new { p.UserId, p.Type });
        builder.Property(p => p.Type).HasConversion<string>().HasMaxLength(50);
        builder.HasIndex(p => p.TenantId);
        builder.HasOne<User>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
