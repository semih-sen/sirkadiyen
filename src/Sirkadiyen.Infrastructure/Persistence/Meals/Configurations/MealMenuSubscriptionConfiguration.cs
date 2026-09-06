using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sirkadiyen.Domain.Identity;
using Sirkadiyen.Domain.Meals;

namespace Sirkadiyen.Infrastructure.Persistence.Meals.Configurations;

internal sealed class MealMenuSubscriptionConfiguration
    : IEntityTypeConfiguration<MealMenuSubscription>
{
    public void Configure(EntityTypeBuilder<MealMenuSubscription> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("meal_menu_subscriptions");
        builder.HasKey(subscription => subscription.Id);
        builder.Property(subscription => subscription.Id).ValueGeneratedNever();

        builder.Property(subscription => subscription.RowVersion).IsRowVersion();

        // One preference per user; the upsert relies on it.
        builder.HasIndex(subscription => subscription.UserId).IsUnique();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(subscription => subscription.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
