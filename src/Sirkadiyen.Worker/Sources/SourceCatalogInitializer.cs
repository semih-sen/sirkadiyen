using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sirkadiyen.Application.Scheduling.Sources;
using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Domain.Scheduling.Sources;
using Sirkadiyen.Infrastructure.Scheduling.Sources;
using Sirkadiyen.Worker.Configuration;

namespace Sirkadiyen.Worker.Sources;

internal sealed class SourceCatalogInitializer(
    IServiceScopeFactory scopeFactory,
    ScheduleSourceCatalogLoader catalogLoader,
    WorkerOptions options,
    ILogger<SourceCatalogInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        ScheduleSourceCatalog catalog = await catalogLoader.LoadAsync(
            options.SourceCatalogPath,
            cancellationToken);
        IReadOnlyCollection<ScheduleSource> sources =
            [.. catalog.Sources.Select(static source => source.ToScheduleSource())];

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IScheduleSourceStore store = scope.ServiceProvider.GetRequiredService<IScheduleSourceStore>();
        int changed = await store.UpsertAsync(sources, cancellationToken);
        logger.LogInformation(
            "Schedule source catalog loaded with {SourceCount} sources; {ChangedCount} rows changed.",
            sources.Count,
            changed);

        ReportAcademicYearDivergences(catalog);
    }

    /// <summary>
    /// Reports any cohort whose catalog sources state a year the deployed profile schema does not
    /// stamp on its students (ADR-115).
    /// </summary>
    /// <remarks>
    /// It logs and does not stop anything, on purpose. The pipeline is not wrong when the two
    /// disagree — every parse, validation and publication is correct — and blocking them would
    /// let one mistyped catalog field take a program offline. What is wrong is that every calendar
    /// in the cohort silently receives nothing, and the only reason that went unnoticed is that
    /// nothing said it out loud. The repair is a profile rollover from <c>/admin/operations</c>.
    /// </remarks>
    private void ReportAcademicYearDivergences(ScheduleSourceCatalog catalog)
    {
        IReadOnlyList<AcademicYearDivergence> divergences =
            SupportedProfileSchemaCatalogCheck.FindDivergences(
                CurrentSupportedProfileSchema.Create(),
                catalog.Sources.Select(source => new CohortPublishedYear
                {
                    AcademicYear = source.AcademicYear,
                    ClassYear = source.ClassYear,
                    ProgramLanguage = source.ProgramLanguage,
                }));

        foreach (AcademicYearDivergence divergence in divergences)
        {
            logger.LogError(
                "Academic year divergence: {Divergence}. Every calendar in this cohort will "
                + "receive no new lesson, because audience resolution matches a record to a "
                + "student only when the years are equal. Run a profile academic-year rollover "
                + "from /admin/operations, or correct the catalog.",
                divergence.ToString());
        }
    }
}
