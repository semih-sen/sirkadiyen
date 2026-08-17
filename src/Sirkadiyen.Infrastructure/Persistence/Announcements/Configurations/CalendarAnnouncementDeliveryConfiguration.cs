using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sirkadiyen.Domain.Announcements;
using Sirkadiyen.Domain.Identity;

namespace Sirkadiyen.Infrastructure.Persistence.Announcements.Configurations;

internal sealed class CalendarAnnouncementDeliveryConfiguration
    : IEntityTypeConfiguration<CalendarAnnouncementDelivery>
{
    public void Configure(EntityTypeBuilder<CalendarAnnouncementDelivery> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("calendar_announcement_deliveries");
        builder.HasKey(delivery => delivery.Id);
        builder.Property(delivery => delivery.Id).ValueGeneratedNever();

        builder.Property(delivery => delivery.State)
            .HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(delivery => delivery.SkipReason)
            .HasConversion<string>().HasMaxLength(40);
        builder.Property(delivery => delivery.GoogleCalendarId)
            .HasMaxLength(CalendarAnnouncementDelivery.MaximumGoogleCalendarIdLength);
        builder.Property(delivery => delivery.GoogleEventId)
            .HasMaxLength(CalendarAnnouncementDelivery.MaximumGoogleEventIdLength);
        builder.Property(delivery => delivery.FailureReason)
            .HasMaxLength(CalendarAnnouncementDelivery.MaximumFailureReasonLength);

        // One copy per recipient per announcement. This is what makes a resumed delivery pass
        // converge instead of writing a second row for someone already written to.
        builder.HasIndex(delivery => new
        {
            delivery.CalendarAnnouncementId,
            delivery.UserId,
        }).IsUnique();

        // The worker's per-announcement queue.
        builder.HasIndex(delivery => new
        {
            delivery.CalendarAnnouncementId,
            delivery.State,
        });

        builder.HasOne<CalendarAnnouncement>()
            .WithMany()
            .HasForeignKey(delivery => delivery.CalendarAnnouncementId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(delivery => delivery.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // A skip has to say why. Without this, "Skipped" rows could accumulate with no reason and
        // the exclusion counters shown to the operator would be unexplainable.
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_calendar_announcement_deliveries_skip_reason",
            "(\"State\" <> 'Skipped') OR (\"SkipReason\" IS NOT NULL)"));
    }
}
