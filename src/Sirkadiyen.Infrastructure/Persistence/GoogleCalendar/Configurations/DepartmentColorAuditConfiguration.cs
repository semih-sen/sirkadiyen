using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sirkadiyen.Domain.GoogleCalendar;

namespace Sirkadiyen.Infrastructure.Persistence.Configurations;

public sealed class DepartmentColorAuditConfiguration
    : IEntityTypeConfiguration<DepartmentColorAudit>
{
    public void Configure(EntityTypeBuilder<DepartmentColorAudit> builder)
    {
        builder.ToTable("department_color_audits");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasColumnName("id");
        builder.Property(item => item.Scope)
            .HasColumnName("scope")
            .HasConversion<string>()
            .HasMaxLength(32);
        builder.Property(item => item.UserId).HasColumnName("user_id");
        builder.Property(item => item.DepartmentKey)
            .HasColumnName("department_key")
            .HasMaxLength(DepartmentColorSetting.MaximumDepartmentKeyLength);
        builder.Property(item => item.PreviousColor)
            .HasColumnName("previous_color")
            .HasMaxLength(DepartmentColorSetting.ColorLength)
            .IsFixedLength();
        builder.Property(item => item.NewColor)
            .HasColumnName("new_color")
            .HasMaxLength(DepartmentColorSetting.ColorLength)
            .IsFixedLength();
        builder.Property(item => item.Actor)
            .HasColumnName("actor")
            .HasMaxLength(DepartmentColorAudit.MaximumActorLength);
        builder.Property(item => item.Reason)
            .HasColumnName("reason")
            .HasMaxLength(DepartmentColorAudit.MaximumReasonLength);
        builder.Property(item => item.CorrelationId)
            .HasColumnName("correlation_id")
            .HasMaxLength(DepartmentColorAudit.MaximumCorrelationIdLength);
        builder.Property(item => item.ChangedAtUtc).HasColumnName("changed_at_utc");
        builder.HasIndex(item => item.ChangedAtUtc);
        builder.HasIndex(item => new { item.UserId, item.ChangedAtUtc });
    }
}
