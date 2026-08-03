using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Domain.GoogleCalendar;
using Sirkadiyen.Domain.Identity;
using Sirkadiyen.Domain.Licensing;
using Sirkadiyen.Domain.Operations;
using Sirkadiyen.Domain.ScheduleDiffing;
using Sirkadiyen.Domain.ScheduleIngestion;
using Sirkadiyen.Domain.ScheduleParsing;
using Sirkadiyen.Domain.SchedulePublication;
using Sirkadiyen.Domain.ScheduleSources;
using Sirkadiyen.Domain.StudentProfiles;

namespace Sirkadiyen.Infrastructure.Persistence;

/// <summary>
/// The PostgreSQL context for identity and the schedule pipeline.
/// </summary>
/// <remarks>
/// Mapping lives in configuration classes rather than in the entities, so the
/// domain project stays free of Entity Framework. The Calendar authorization a user
/// grants is stored here, along with the durable calendar-event mappings the initial
/// sync writes (ADR-058); the incremental sync job state remains deliberately absent
/// until its accepted design is implemented.
/// </remarks>
public sealed class SirkadiyenDbContext(DbContextOptions<SirkadiyenDbContext> options)
    : DbContext(options)
{
    public const string SchemaName = "sirkadiyen";

    public DbSet<User> Users => Set<User>();

    public DbSet<License> Licenses => Set<License>();

    public DbSet<LicenseAudit> LicenseAudits => Set<LicenseAudit>();

    public DbSet<StudentProfile> StudentProfiles => Set<StudentProfile>();

    public DbSet<GoogleCalendarConnection> GoogleCalendarConnections =>
        Set<GoogleCalendarConnection>();

    public DbSet<UserCalendarEventMapping> UserCalendarEventMappings =>
        Set<UserCalendarEventMapping>();

    public DbSet<DepartmentColorSetting> DepartmentColorSettings =>
        Set<DepartmentColorSetting>();

    public DbSet<UserDepartmentColorPreference> UserDepartmentColorPreferences =>
        Set<UserDepartmentColorPreference>();

    public DbSet<DepartmentColorAudit> DepartmentColorAudits =>
        Set<DepartmentColorAudit>();

    public DbSet<ScheduleSource> ScheduleSources => Set<ScheduleSource>();

    public DbSet<SourceSnapshot> SourceSnapshots => Set<SourceSnapshot>();

    public DbSet<SourceDocumentUpload> SourceDocumentUploads => Set<SourceDocumentUpload>();

    public DbSet<ParseRun> ParseRuns => Set<ParseRun>();

    public DbSet<ScheduleRevision> ScheduleRevisions => Set<ScheduleRevision>();

    public DbSet<CanonicalScheduleRecord> CanonicalScheduleRecords =>
        Set<CanonicalScheduleRecord>();

    public DbSet<RevisionValidationFinding> RevisionValidationFindings =>
        Set<RevisionValidationFinding>();

    public DbSet<ScheduleDiff> ScheduleDiffs => Set<ScheduleDiff>();

    public DbSet<ScheduleDiffEntry> ScheduleDiffEntries => Set<ScheduleDiffEntry>();

    public DbSet<OperationalFreezeControl> OperationalFreezeControls =>
        Set<OperationalFreezeControl>();

    public DbSet<OperationalFreezeAudit> OperationalFreezeAudits =>
        Set<OperationalFreezeAudit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SirkadiyenDbContext).Assembly);
    }
}
