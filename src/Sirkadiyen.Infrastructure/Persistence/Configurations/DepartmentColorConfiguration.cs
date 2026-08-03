using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sirkadiyen.Domain.GoogleCalendar;

namespace Sirkadiyen.Infrastructure.Persistence.Configurations;

public sealed class DepartmentColorSettingConfiguration
    : IEntityTypeConfiguration<DepartmentColorSetting>
{
    public void Configure(EntityTypeBuilder<DepartmentColorSetting> builder)
    {
        builder.ToTable("department_color_settings");
        builder.HasKey(item => item.DepartmentKey);
        builder.Property(item => item.DepartmentKey)
            .HasColumnName("department_key")
            .HasMaxLength(DepartmentColorSetting.MaximumDepartmentKeyLength);
        builder.Property(item => item.BackgroundColor)
            .HasColumnName("background_color")
            .HasMaxLength(DepartmentColorSetting.ColorLength)
            .IsFixedLength();
        builder.Property(item => item.UpdatedBy)
            .HasColumnName("updated_by")
            .HasMaxLength(DepartmentColorAudit.MaximumActorLength);
        builder.Property(item => item.UpdatedAtUtc).HasColumnName("updated_at_utc");
        builder.Property(item => item.RowVersion)
            .HasColumnName("xmin")
            .IsRowVersion();
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_department_color_settings_color",
            "background_color ~ '^#[0-9A-F]{6}$'"));
    }
}

public sealed class UserDepartmentColorPreferenceConfiguration
    : IEntityTypeConfiguration<UserDepartmentColorPreference>
{
    public void Configure(EntityTypeBuilder<UserDepartmentColorPreference> builder)
    {
        builder.ToTable("user_department_color_preferences");
        builder.HasKey(item => new { item.UserId, item.DepartmentKey });
        builder.Property(item => item.UserId).HasColumnName("user_id");
        builder.Property(item => item.DepartmentKey)
            .HasColumnName("department_key")
            .HasMaxLength(DepartmentColorSetting.MaximumDepartmentKeyLength);
        builder.Property(item => item.BackgroundColor)
            .HasColumnName("background_color")
            .HasMaxLength(DepartmentColorSetting.ColorLength)
            .IsFixedLength();
        builder.Property(item => item.UpdatedAtUtc).HasColumnName("updated_at_utc");
        builder.HasOne<Sirkadiyen.Domain.Identity.User>()
            .WithMany()
            .HasForeignKey(item => item.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_user_department_color_preferences_color",
            "background_color ~ '^#[0-9A-F]{6}$'"));
    }
}

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
