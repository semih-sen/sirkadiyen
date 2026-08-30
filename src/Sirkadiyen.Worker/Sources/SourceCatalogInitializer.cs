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
        await InstallShippedCatalogAsync(cancellationToken);

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
    /// Installs the catalog this release shipped, through the same path an administrative edit
    /// takes (ADR-138).
    /// </summary>
    /// <remarks>
    /// The deployed artifact carries the repository's catalog; the running one lives in shared
    /// configuration, outside every release directory, and used to be seeded only when it did not
    /// exist. So a catalog change that was committed, reviewed and merged did not reach the server
    /// at all — it had to be re-typed into the panel — and until someone did, the deployed code and
    /// the running configuration disagreed with nothing saying so.
    /// <para>
    /// Applying it here rather than from the deployment script means it is validated by the loader
    /// the worker itself uses, the source rows move with it in one transaction, and it appears in
    /// the catalog history as a <c>Deployment</c> revision carrying the document it replaced. A
    /// panel edit that was never committed is therefore replaced by the next deployment, and is
    /// restorable from that revision.
    /// </para>
    /// <para>
    /// Failure here is logged and does not stop startup. The worker's job is to poll the catalog it
    /// has, and refusing to start because a shipped document could not be installed would turn a
    /// configuration problem into an outage.
    /// </para>
    /// </remarks>
    private async Task InstallShippedCatalogAsync(CancellationToken cancellationToken)
    {
        if (options.ShippedSourceCatalogPath is not { } shippedPath
            || string.Equals(shippedPath, options.SourceCatalogPath, StringComparison.Ordinal))
        {
            // A deployment that ships no catalog, or a development run where the shipped file and
            // the live file are the same file. Nothing to install.
            return;
        }

        if (!File.Exists(shippedPath))
        {
            logger.LogWarning(
                "This release ships no schedule source catalog at {ShippedPath}, so the running "
                + "catalog is left as it is.",
                shippedPath);
            return;
        }

        try
        {
            string shipped = await File.ReadAllTextAsync(shippedPath, cancellationToken);

            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            ScheduleSourceCatalogEditingService editing = scope.ServiceProvider
                .GetRequiredService<ScheduleSourceCatalogEditingService>();

            ScheduleSourceCatalogDeploymentResult result = await editing.ApplyFromDeploymentAsync(
                shipped,
                options.Release,
                correlationId: null,
                cancellationToken);

            if (!result.Applied)
            {
                logger.LogInformation(
                    "The catalog shipped with release {Release} is already the running catalog.",
                    options.Release);
                return;
            }

            logger.LogWarning(
                "The catalog shipped with release {Release} replaced the running one: "
                + "{Added} source(s) added, {Removed} removed, {Modified} modified. "
                + "Revision {RevisionId} carries the document it replaced.",
                options.Release,
                result.Plan.Added.Count,
                result.Plan.Removed.Count,
                result.Plan.Modified.Count,
                result.RevisionId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "The catalog shipped with release {Release} could not be installed. The worker "
                + "continues with the catalog already on the server.",
                options.Release);
        }
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
