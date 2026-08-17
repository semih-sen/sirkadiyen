using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sirkadiyen.Domain.Announcements;
using Sirkadiyen.Domain.Identity;
using Sirkadiyen.Infrastructure.Persistence.StudentProfiles.Configurations;

namespace Sirkadiyen.Infrastructure.Persistence.Announcements.Configurations;

internal sealed class CalendarAnnouncementConfiguration
    : IEntityTypeConfiguration<CalendarAnnouncement>
{
    public void Configure(EntityTypeBuilder<CalendarAnnouncement> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("calendar_announcements");
        builder.HasKey(announcement => announcement.Id);
        builder.Property(announcement => announcement.Id).ValueGeneratedNever();

        builder.Property(announcement => announcement.Kind)
            .HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(announcement => announcement.Status)
            .HasConversion<string>().HasMaxLength(30).IsRequired();

        builder.Property(announcement => announcement.CampaignKey)
            .HasMaxLength(AnnouncementCampaignKey.MaximumLength).IsRequired();
        builder.Property(announcement => announcement.TemplateKey)
            .HasMaxLength(AnnouncementCampaignKey.MaximumTemplateKeyLength);
        builder.Property(announcement => announcement.Title)
            .HasMaxLength(CalendarAnnouncement.MaximumTitleLength).IsRequired();
        builder.Property(announcement => announcement.Body)
            .HasMaxLength(CalendarAnnouncement.MaximumBodyLength).IsRequired();
        builder.Property(announcement => announcement.Location)
            .HasMaxLength(CalendarAnnouncement.MaximumLocationLength);
        builder.Property(announcement => announcement.TimeZoneId)
            .HasMaxLength(CalendarAnnouncement.MaximumTimeZoneIdLength).IsRequired();
        builder.Property(announcement => announcement.CategoryKey)
            .HasMaxLength(CalendarAnnouncement.MaximumCategoryKeyLength).IsRequired();
        builder.Property(announcement => announcement.InternalNote)
            .HasMaxLength(CalendarAnnouncement.MaximumInternalNoteLength);

        builder.Property(announcement => announcement.AudienceAcademicYear)
            .HasMaxLength(CalendarAnnouncement.MaximumAcademicYearLength).IsRequired();
        builder.Property(announcement => announcement.AudienceProgramLanguage)
            .HasConversion<string>().HasMaxLength(20);

        // The same JSONB shape and comparer as a student profile's selectors, because they are
        // compared against each other: an audience selector is matched to a profile selector.
        builder.Property(announcement => announcement.AudienceSelectors)
            .HasConversion(new ProfileSelectorsConverter())
            .HasColumnType("jsonb")
            .IsRequired()
            .Metadata.SetValueComparer(new ProfileSelectorsComparer());

        builder.Property(announcement => announcement.PlanHash).HasMaxLength(128);
        builder.Property(announcement => announcement.CreatedBy)
            .HasMaxLength(CalendarAnnouncement.MaximumActorLength).IsRequired();
        builder.Property(announcement => announcement.CreationReason)
            .HasMaxLength(CalendarAnnouncement.MaximumReasonLength).IsRequired();
        builder.Property(announcement => announcement.LastUpdatedBy)
            .HasMaxLength(CalendarAnnouncement.MaximumActorLength);
        builder.Property(announcement => announcement.LastUpdateReason)
            .HasMaxLength(CalendarAnnouncement.MaximumReasonLength);
        builder.Property(announcement => announcement.CancelledBy)
            .HasMaxLength(CalendarAnnouncement.MaximumActorLength);
        builder.Property(announcement => announcement.CancellationReason)
            .HasMaxLength(CalendarAnnouncement.MaximumReasonLength);
        builder.Property(announcement => announcement.LastFailureReason)
            .HasMaxLength(CalendarAnnouncement.MaximumFailureReasonLength);

        builder.Property(announcement => announcement.RowVersion).IsRowVersion();

        // The deduplication guarantee is the index, not the application check: two operators
        // confirming the same announcement concurrently must not both win (plan §4.4).
        builder.HasIndex(announcement => announcement.CampaignKey).IsUnique();

        // The delivery worker's queue: everything not yet finished, oldest first.
        builder.HasIndex(announcement => new
        {
            announcement.Status,
            announcement.NextAttemptAtUtc,
        });

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(announcement => announcement.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // A warning names its recipient; deleting the account cascades the announcement away with
        // it, because a warning addressed to nobody has no meaning.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(announcement => announcement.TargetUserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_calendar_announcements_times",
            "(\"IsAllDay\" AND \"StartLocalTime\" IS NULL AND \"EndLocalTime\" IS NULL) "
            + "OR (NOT \"IsAllDay\" AND \"StartLocalTime\" IS NOT NULL "
            + "AND \"EndLocalTime\" IS NOT NULL)"));

        // A warning is addressed to exactly one user and a bulk announcement to none in
        // particular. Pinning it here means no code path can produce the other combination.
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_calendar_announcements_target",
            "(\"Kind\" = 'UserWarning' AND \"TargetUserId\" IS NOT NULL) "
            + "OR (\"Kind\" = 'Bulk' AND \"TargetUserId\" IS NULL)"));
    }
}
