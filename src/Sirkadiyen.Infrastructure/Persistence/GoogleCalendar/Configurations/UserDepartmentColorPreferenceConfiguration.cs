using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sirkadiyen.Domain.GoogleCalendar;

namespace Sirkadiyen.Infrastructure.Persistence.Configurations;

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
