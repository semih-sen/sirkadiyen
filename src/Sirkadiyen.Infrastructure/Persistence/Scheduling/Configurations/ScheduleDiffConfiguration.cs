using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sirkadiyen.Domain.ScheduleDiffing;
using Sirkadiyen.Domain.SchedulePublication;
using Sirkadiyen.Domain.ScheduleSources;

namespace Sirkadiyen.Infrastructure.Persistence.Configurations;

internal sealed class ScheduleDiffConfiguration : IEntityTypeConfiguration<ScheduleDiff>
{
    public void Configure(EntityTypeBuilder<ScheduleDiff> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("schedule_diffs");
        builder.HasKey(diff => diff.Id);

        builder.Property(diff => diff.SourceId)
            .HasConversion(new SourceIdConverter())
            .HasMaxLength(SourceId.MaxLength)
            .IsRequired();

        // Stored as text so that adding a state later cannot renumber the ones
        // already written (AI_GUIDELINE section 18).
        builder.Property(diff => diff.State).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(diff => diff.HoldReason)
            .HasMaxLength(ScheduleDiff.MaximumHoldReasonLength);

        // Who released a held diff, and why (ADR-042). The hold reason is kept
        // alongside, so a released diff still says what it was held for.
        builder.Property(diff => diff.ReleasedBy)
            .HasMaxLength(ScheduleDiff.MaximumReleasedByLength);
        builder.Property(diff => diff.ReleaseReason)
            .HasMaxLength(ScheduleDiff.MaximumReleaseReasonLength);

        // Release is the last gate before student calendars change, so two
        // operators acting at once must not silently overwrite each other.
        builder.Property(diff => diff.RowVersion).IsRowVersion();

        // How far the diff has been fanned out onto calendars (ADR-059), stored as text for the
        // same forward-compatibility reason as State.
        builder.Property(diff => diff.CalendarDispatchState)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(diff => diff.DispatchFailureReason)
            .HasMaxLength(ScheduleDiff.MaximumDispatchFailureReasonLength);

        // Who returned a terminally failed diff to the queue, and why (ADR-097). The count is kept
        // across retries even though the attempts are reset, so a diff being retried repeatedly is
        // visible as such.
        builder.Property(diff => diff.DispatchRetryCount)
            .IsRequired();
        builder.Property(diff => diff.LastDispatchRetriedBy)
            .HasMaxLength(ScheduleDiff.MaximumDispatchRetriedByLength);
        builder.Property(diff => diff.LastDispatchRetryReason)
            .HasMaxLength(ScheduleDiff.MaximumDispatchRetryReasonLength);
        builder.Property(diff => diff.LastDispatchRetriedAtUtc);

        builder.Ignore(diff => diff.IsDispatchable);
        builder.Ignore(diff => diff.IsReleasable);
        builder.Ignore(diff => diff.IsDispatchPending);
        builder.Ignore(diff => diff.IsDispatchRetriable);

        builder.HasOne<ScheduleSource>()
            .WithMany()
            .HasForeignKey(diff => diff.ScheduleSourceId)
            .OnDelete(DeleteBehavior.Restrict);

        // A diff is evidence for what was written to student calendars, so
        // neither revision it names may be deleted while it exists.
        builder.HasOne<ScheduleRevision>()
            .WithMany()
            .HasForeignKey(diff => diff.CurrentRevisionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ScheduleRevision>()
            .WithMany()
            .HasForeignKey(diff => diff.PreviousRevisionId)
            .OnDelete(DeleteBehavior.Restrict);

        // Exactly one diff per published revision. Calculation is retried after
        // a crash, and this is what makes the retry idempotent rather than
        // producing a second set of calendar operations.
        builder.HasIndex(diff => diff.CurrentRevisionId).IsUnique();

        // The dispatcher scans for ready diffs and the review queue for held
        // ones, both oldest first.
        builder.HasIndex(diff => new { diff.State, diff.CreatedAtUtc });

        // The incremental dispatcher scans for pending diffs whose back-off has passed (ADR-059).
        builder.HasIndex(diff => new { diff.CalendarDispatchState, diff.NextAttemptAtUtc });

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_schedule_diffs_calendar_dispatch_state",
            "\"CalendarDispatchState\" IN ('Pending', 'Dispatched', 'Failed')"));

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_schedule_diffs_dispatch_retry_count",
            "\"DispatchRetryCount\" >= 0"));

        builder.HasMany(diff => diff.Entries)
            .WithOne()
            .HasForeignKey(entry => entry.ScheduleDiffId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata
            .FindNavigation(nameof(ScheduleDiff.Entries))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
