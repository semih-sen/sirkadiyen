using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sirkadiyen.Domain.Meals;

namespace Sirkadiyen.Infrastructure.Persistence.Meals.Configurations;

internal sealed class MealMenuDayConfiguration : IEntityTypeConfiguration<MealMenuDay>
{
    public void Configure(EntityTypeBuilder<MealMenuDay> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("meal_menu_days");
        builder.HasKey(day => day.Id);
        builder.Property(day => day.Id).ValueGeneratedNever();

        builder.Property(day => day.Category)
            .HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(day => day.Status)
            .HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(day => day.MealText)
            .HasMaxLength(MealMenuDay.MaximumMealTextLength).IsRequired();
        builder.Property(day => day.ContentHash)
            .HasMaxLength(MealMenuDay.MaximumContentHashLength).IsRequired();

        builder.Property(day => day.RowVersion).IsRowVersion();

        // One menu per date and meal. This is what makes a re-poll update the same day instead of
        // appending a second row for it (AI_GUIDELINE §18).
        builder.HasIndex(day => new { day.LocalDate, day.Category }).IsUnique();

        // The acquisition and delivery window scan: published days in a date range for one meal.
        builder.HasIndex(day => new { day.Category, day.Status, day.LocalDate });
    }
}
