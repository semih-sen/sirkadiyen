using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Domain.ScheduleDiffing;
using Sirkadiyen.Domain.ScheduleIngestion;
using Sirkadiyen.Domain.ScheduleParsing;
using Sirkadiyen.Domain.SchedulePublication;
using Sirkadiyen.Domain.ScheduleSources;

namespace Sirkadiyen.Infrastructure.Persistence;

/// <summary>
/// The PostgreSQL context for the schedule ingestion and publication pipeline.
/// </summary>
/// <remarks>
/// Mapping lives in configuration classes rather than in the entities, so the
/// domain project stays free of Entity Framework. Identity, licensing and
/// calendar synchronization tables are deliberately absent: their behaviour is
/// still an open decision, and a migration is far more expensive to change than
/// a class.
/// </remarks>
public sealed class SirkadiyenDbContext(DbContextOptions<SirkadiyenDbContext> options)
    : DbContext(options)
{
    public const string SchemaName = "sirkadiyen";

    public DbSet<ScheduleSource> ScheduleSources => Set<ScheduleSource>();

    public DbSet<SourceSnapshot> SourceSnapshots => Set<SourceSnapshot>();

    public DbSet<ParseRun> ParseRuns => Set<ParseRun>();

    public DbSet<ScheduleRevision> ScheduleRevisions => Set<ScheduleRevision>();

    public DbSet<CanonicalScheduleRecord> CanonicalScheduleRecords =>
        Set<CanonicalScheduleRecord>();

    public DbSet<RevisionValidationFinding> RevisionValidationFindings =>
        Set<RevisionValidationFinding>();

    public DbSet<ScheduleDiff> ScheduleDiffs => Set<ScheduleDiff>();

    public DbSet<ScheduleDiffEntry> ScheduleDiffEntries => Set<ScheduleDiffEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SirkadiyenDbContext).Assembly);
    }
}
