using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sirkadiyen.Domain.Scheduling.Ingestion;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Infrastructure.Persistence.Scheduling.Configurations;

internal sealed class SourcePollRequestConfiguration : IEntityTypeConfiguration<SourcePollRequest>
{
    public void Configure(EntityTypeBuilder<SourcePollRequest> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("source_poll_requests");
        builder.HasKey(request => request.Id);

        builder.Property(request => request.SourceId)
            .HasConversion(new SourceIdConverter())
            .HasMaxLength(SourceId.MaxLength)
            .IsRequired();

        builder.Property(request => request.Force).IsRequired();

        builder.Property(request => request.RequestedBy)
            .HasMaxLength(SourcePollRequest.MaximumRequestedByLength)
            .IsRequired();

        builder.Property(request => request.RequestedAtUtc).IsRequired();
        builder.Property(request => request.ClaimedAtUtc);

        // The worker claims the oldest unclaimed requests first, so this is the queue's scan index.
        builder.HasIndex(request => new { request.ClaimedAtUtc, request.RequestedAtUtc });
    }
}
