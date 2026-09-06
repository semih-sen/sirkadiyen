using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sirkadiyen.Domain.Identity;
using Sirkadiyen.Domain.Meals;

namespace Sirkadiyen.Infrastructure.Persistence.Meals.Configurations;

internal sealed class MealCalendarDeliveryConfiguration
    : IEntityTypeConfiguration<MealCalendarDelivery>
{
    public void Configure(EntityTypeBuilder<MealCalendarDelivery> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("meal_calendar_deliveries");
        builder.HasKey(delivery => delivery.Id);
        builder.Property(delivery => delivery.Id).ValueGeneratedNever();

        builder.Property(delivery => delivery.Category)
            .HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(delivery => delivery.State)
            .HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(delivery => delivery.SkipReason)
            .HasConversion<string>().HasMaxLength(40);
        builder.Property(delivery => delivery.GoogleCalendarId)
            .HasMaxLength(MealCalendarDelivery.MaximumGoogleCalendarIdLength);
        builder.Property(delivery => delivery.GoogleEventId)
            .HasMaxLength(MealCalendarDelivery.MaximumGoogleEventIdLength);
        builder.Property(delivery => delivery.FailureReason)
            .HasMaxLength(MealCalendarDelivery.MaximumFailureReasonLength);

        builder.Property(delivery => delivery.RowVersion).IsRowVersion();

        // One copy per subscriber per date per meal. This is what makes a resumed convergence pass
        // reconcile instead of writing a second row for a day already written to.
        builder.HasIndex(delivery => new
        {
            delivery.UserId,
            delivery.LocalDate,
            delivery.Category,
        }).IsUnique();

        // The convergence queues: what to write in a window, and what to remove anywhere.
        builder.HasIndex(delivery => new { delivery.Category, delivery.State, delivery.LocalDate });

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(delivery => delivery.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // A skip has to say why, so the exclusion counters stay explainable (mirrors ADR-107).
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_meal_calendar_deliveries_skip_reason",
            "(\"State\" <> 'Skipped') OR (\"SkipReason\" IS NOT NULL)"));
    }
}
