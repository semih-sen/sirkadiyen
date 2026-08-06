using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sirkadiyen.Domain.GoogleCalendar;
using Sirkadiyen.Domain.Identity;
using Sirkadiyen.Domain.Scheduling.Sources;
using Sirkadiyen.Infrastructure.Persistence.Scheduling.Configurations;

namespace Sirkadiyen.Infrastructure.Persistence.GoogleCalendar.Configurations;

internal sealed class UserCalendarEventMappingConfiguration
    : IEntityTypeConfiguration<UserCalendarEventMapping>
{
    public void Configure(EntityTypeBuilder<UserCalendarEventMapping> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("user_calendar_event_mappings");
        builder.HasKey(mapping => mapping.Id);
        builder.Property(mapping => mapping.Id).ValueGeneratedNever();

        builder.Property(mapping => mapping.StableIdentity)
            .HasMaxLength(UserCalendarEventMapping.MaximumStableIdentityLength)
            .IsRequired();
        builder.Property(mapping => mapping.SourceId)
            .HasConversion(new SourceIdConverter())
            .HasMaxLength(SourceId.MaxLength)
            .IsRequired();
        builder.Property(mapping => mapping.GoogleCalendarId)
            .HasMaxLength(UserCalendarEventMapping.MaximumGoogleCalendarIdLength)
            .IsRequired();
        builder.Property(mapping => mapping.GoogleEventId)
            .HasMaxLength(UserCalendarEventMapping.MaximumGoogleEventIdLength)
            .IsRequired();
        builder.Property(mapping => mapping.ContentHash)
            .HasMaxLength(UserCalendarEventMapping.MaximumContentHashLength)
            .IsRequired();
        builder.Property(mapping => mapping.RowVersion).IsRowVersion();

        // One event per logical lesson per user: the mapping is the idempotency ledger, so the
        // schema forbids writing the same lesson to a user's calendar twice (ADR-058).
        builder.HasIndex(mapping => new { mapping.UserId, mapping.StableIdentity }).IsUnique();
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(mapping => mapping.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
