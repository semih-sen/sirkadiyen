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
