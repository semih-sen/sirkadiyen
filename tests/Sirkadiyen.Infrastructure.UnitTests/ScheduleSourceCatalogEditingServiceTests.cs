using System.Text.Json;
using Sirkadiyen.Application.Scheduling.Sources;
using Sirkadiyen.Domain.Scheduling.Sources;
using Sirkadiyen.Infrastructure.Scheduling.Sources;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>
/// Covers the administrative catalog editor (ADR-114). The catalog decides which document belongs
/// to which program and which parser reads it, so the rules under test are the ones that stop an
/// edit from moving a cohort's lessons without anybody noticing: the plan hash, the on-disk hash,
/// the refusal of a document the worker could not load, and the file rollback when the database
/// commit fails.
/// </summary>
public sealed class ScheduleSourceCatalogEditingServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReadReturnsAnUnparseableDocumentWithItsReasonAsync()
    {
        // The editor is the tool for repairing a broken catalog, so refusing to show one would
        // leave a server shell as the only repair path.
        FakeCatalogFile file = new("{ this is not json");
        ScheduleSourceCatalogEditingService service = Service(file, out _);

        ScheduleSourceCatalogDocument document = await service.ReadAsync(CancellationToken.None);

        Assert.False(document.IsValid);
        Assert.NotNull(document.ValidationError);
        Assert.Equal("{ this is not json", document.Content);
        Assert.Null(document.SourceCount);
    }

    [Fact]
    public async Task PreviewClassifiesADisplayNameChangeAsLowRiskAsync()
    {
        FakeCatalogFile file = new(Catalog(Source()));
        ScheduleSourceCatalogEditingService service = Service(file, out _);

        ScheduleSourceCatalogPlan plan = await service.PreviewAsync(
            Catalog(Source(displayName: "Yeni ad")),
            file.Content.Hash,
            CancellationToken.None);

        ScheduleSourceCatalogSourceChange change = Assert.Single(plan.Modified);
        ScheduleSourceCatalogFieldChange field = Assert.Single(change.Fields);
        Assert.Equal("displayName", field.Field);
        Assert.Equal(ScheduleSourceCatalogChangeRisk.Low, field.Risk);
        Assert.False(plan.HasHighRiskChange);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public async Task PreviewFlagsARetargetedAudienceAsHighRiskAsync()
    {
        // The dangerous edit: nothing about the document or the parse changes, but every lesson
        // this source has already published now belongs to a different cohort.
        FakeCatalogFile file = new(Catalog(Source()));
        ScheduleSourceCatalogEditingService service = Service(file, out _);

        ScheduleSourceCatalogPlan plan = await service.PreviewAsync(
            Catalog(Source(classYear: 4)),
            file.Content.Hash,
            CancellationToken.None);

        Assert.True(plan.HasHighRiskChange);
        Assert.Contains(plan.Warnings, warning => warning.Code == "audience-retargeted");
    }

    [Fact]
    public async Task PreviewRefusesADocumentTheWorkerCouldNotLoadAsync()
    {
        // Same loader as the worker's startup path, so an accepted edit can never be a catalog
        // the worker refuses to start on.
        FakeCatalogFile file = new(Catalog(Source()));
        ScheduleSourceCatalogEditingService service = Service(file, out _);

        ScheduleSourceCatalogValidationException exception =
            await Assert.ThrowsAsync<ScheduleSourceCatalogValidationException>(
                () => service.PreviewAsync(
                    Catalog(Source(classYear: 9)),
                    file.Content.Hash,
                    CancellationToken.None));

        Assert.Contains("unsupported class year", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreviewRefusesADocumentWithAnUnknownPropertyAsync()
    {
        // A mistyped property would deserialize to nothing and validate cleanly, leaving the
        // source configured exactly as before with no sign the edit did not take.
        FakeCatalogFile file = new(Catalog(Source()));
        ScheduleSourceCatalogEditingService service = Service(file, out _);

        string typo = Catalog(Source()).Replace(
            "\"parserProfileVersion\"",
            "\"parserProfileVersionn\"",
            StringComparison.Ordinal);

        await Assert.ThrowsAsync<ScheduleSourceCatalogValidationException>(
            () => service.PreviewAsync(typo, file.Content.Hash, CancellationToken.None));
    }

    [Fact]
    public async Task PreviewRefusesAStaleBaseHashAsync()
    {
        FakeCatalogFile file = new(Catalog(Source()));
        ScheduleSourceCatalogEditingService service = Service(file, out _);

        await Assert.ThrowsAsync<ScheduleSourceCatalogConflictException>(
            () => service.PreviewAsync(
                Catalog(Source(displayName: "Yeni ad")),
                ScheduleSourceCatalogPlanner.Hash("someone else's catalog"),
                CancellationToken.None));
    }

    [Fact]
    public async Task ApplyWritesTheDocumentAndCommitsItsSourcesAsync()
    {
        FakeCatalogFile file = new(Catalog(Source()));
        ScheduleSourceCatalogEditingService service = Service(file, out FakeRevisionStore revisions);
        string proposed = Catalog(Source(displayName: "Yeni ad"));
        ScheduleSourceCatalogPlan plan = await service.PreviewAsync(
            proposed,
            file.Content.Hash,
            CancellationToken.None);

        ScheduleSourceCatalogApplyResult result = await service.ApplyAsync(
            Command(proposed, plan),
            CancellationToken.None);

        Assert.Equal(ScheduleSourceCatalogPlanner.Normalize(proposed), file.Content.Text);
        Assert.Equal(result.ContentHash, file.Content.Hash);
        ScheduleSourceCatalogCommit commit = Assert.Single(revisions.Commits);
        Assert.Equal(ScheduleSourceCatalogRevisionKind.Edit, commit.Revision.Kind);
        Assert.Equal("Kaynak adı düzeltildi", commit.Revision.Reason);
        Assert.Single(commit.Sources);
        Assert.Empty(commit.PollingDisabled);
    }

    [Fact]
    public async Task ApplyRecordsTheStateBeforeTheFirstEditAsTheBaselineAsync()
    {
        // Without it the oldest restorable document would be the result of the first edit, and
        // the state the system actually started from would exist nowhere.
        string original = Catalog(Source());
        FakeCatalogFile file = new(original);
        ScheduleSourceCatalogEditingService service = Service(file, out FakeRevisionStore revisions);
        string proposed = Catalog(Source(displayName: "Yeni ad"));
        ScheduleSourceCatalogPlan plan = await service.PreviewAsync(
            proposed,
            file.Content.Hash,
            CancellationToken.None);

        await service.ApplyAsync(Command(proposed, plan), CancellationToken.None);

        ScheduleSourceCatalogBaselineDraft baseline =
            Assert.Single(revisions.Commits).Baseline!;
        Assert.Equal(original, baseline.Content);
    }

    [Fact]
    public async Task ApplyRetiresASourceTheDocumentNoLongerDeclaresAsync()
    {
        // Dropped from configuration is not a publication decision: polling stops, nothing the
        // source published is deleted (AI_GUIDELINE §13).
        FakeCatalogFile file = new(Catalog(Source(), Source("G1-TR-PRACTICE")));
        ScheduleSourceCatalogEditingService service = Service(file, out FakeRevisionStore revisions);
        string proposed = Catalog(Source());
        ScheduleSourceCatalogPlan plan = await service.PreviewAsync(
            proposed,
            file.Content.Hash,
            CancellationToken.None);

        ScheduleSourceCatalogApplyResult result = await service.ApplyAsync(
            Command(proposed, plan),
            CancellationToken.None);

        Assert.Equal(["G1-TR-PRACTICE"], result.PollingDisabledSourceIds);
        Assert.Equal(
            [SourceId.Parse("G1-TR-PRACTICE")],
            Assert.Single(revisions.Commits).PollingDisabled);
        Assert.Contains(plan.Warnings, warning => warning.Code == "sources-removed");
    }

    [Fact]
    public async Task ApplyRefusesAConfirmationThatDoesNotMatchItsPlanAsync()
    {
        // The operator approved a plan, not a text box. A confirmation carrying another plan's
        // hash authorizes a change nobody was shown.
        FakeCatalogFile file = new(Catalog(Source()));
        ScheduleSourceCatalogEditingService service = Service(file, out FakeRevisionStore revisions);
        string proposed = Catalog(Source(displayName: "Yeni ad"));
        ScheduleSourceCatalogPlan plan = await service.PreviewAsync(
            proposed,
            file.Content.Hash,
            CancellationToken.None);

        await Assert.ThrowsAsync<ScheduleSourceCatalogConflictException>(
            () => service.ApplyAsync(
                // A different document than the one the plan was computed for.
                Command(Catalog(Source(classYear: 2)), plan),
                CancellationToken.None));

        Assert.Empty(revisions.Commits);
        Assert.Equal(Catalog(Source()), file.Content.Text);
    }

    [Fact]
    public async Task ApplyRefusesWhenSomeoneElseChangedTheFileMeanwhileAsync()
    {
        FakeCatalogFile file = new(Catalog(Source()));
        ScheduleSourceCatalogEditingService service = Service(file, out FakeRevisionStore revisions);
        string proposed = Catalog(Source(displayName: "Yeni ad"));
        ScheduleSourceCatalogPlan plan = await service.PreviewAsync(
            proposed,
            file.Content.Hash,
            CancellationToken.None);

        await file.WriteAsync(Catalog(Source(displayName: "Başka biri")), CancellationToken.None);

        await Assert.ThrowsAsync<ScheduleSourceCatalogConflictException>(
            () => service.ApplyAsync(Command(proposed, plan), CancellationToken.None));

        Assert.Empty(revisions.Commits);
    }

    [Fact]
    public async Task ApplyRequiresAReasonAsync()
    {
        FakeCatalogFile file = new(Catalog(Source()));
        ScheduleSourceCatalogEditingService service = Service(file, out _);
        string proposed = Catalog(Source(displayName: "Yeni ad"));
        ScheduleSourceCatalogPlan plan = await service.PreviewAsync(
            proposed,
            file.Content.Hash,
            CancellationToken.None);

        await Assert.ThrowsAsync<ScheduleSourceCatalogValidationException>(
            () => service.ApplyAsync(
                Command(proposed, plan) with { Reason = "   " },
                CancellationToken.None));
    }

    [Fact]
    public async Task ApplyRefusesADocumentIdenticalToTheOneOnDiskAsync()
    {
        FakeCatalogFile file = new(Catalog(Source()));
        ScheduleSourceCatalogEditingService service = Service(file, out _);

        ScheduleSourceCatalogValidationException exception =
            await Assert.ThrowsAsync<ScheduleSourceCatalogValidationException>(
                () => service.ApplyAsync(
                    new ScheduleSourceCatalogApplyCommand
                    {
                        Content = Catalog(Source()),
                        BaseContentHash = file.Content.Hash,
                        PlanHash = ScheduleSourceCatalogPlanner.PlanHash(
                            file.Content.Hash,
                            ScheduleSourceCatalogPlanner.Hash(
                                ScheduleSourceCatalogPlanner.Normalize(Catalog(Source())))),
                        Reason = "Hiçbir şey",
                        ActorUserId = Guid.NewGuid(),
                        ActorEmail = "admin@example.com",
                    },
                    CancellationToken.None));

        Assert.Contains("aynı", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailedCommitPutsThePreviousDocumentBackAsync()
    {
        // The worker reads the file, the pipeline reads the database. A file the database never
        // heard of would take effect at the next worker restart, silently.
        string original = Catalog(Source());
        FakeCatalogFile file = new(original);
        ScheduleSourceCatalogEditingService service = Service(
            file,
            out FakeRevisionStore revisions);
        revisions.Fail = true;
        string proposed = Catalog(Source(displayName: "Yeni ad"));
        ScheduleSourceCatalogPlan plan = await service.PreviewAsync(
            proposed,
            file.Content.Hash,
            CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ApplyAsync(Command(proposed, plan), CancellationToken.None));

        Assert.Equal(original, file.Content.Text);
    }

    private static ScheduleSourceCatalogApplyCommand Command(
        string content,
        ScheduleSourceCatalogPlan plan) => new()
        {
            Content = content,
            BaseContentHash = plan.BaseContentHash,
            PlanHash = plan.PlanHash,
            Reason = "Kaynak adı düzeltildi",
            ActorUserId = Guid.NewGuid(),
            ActorEmail = "admin@example.com",
        };

    private static ScheduleSourceCatalogEditingService Service(
        FakeCatalogFile file,
        out FakeRevisionStore revisions)
    {
        revisions = new FakeRevisionStore();
        return new ScheduleSourceCatalogEditingService(
            file,
            new ScheduleSourceCatalogLoader(),
            revisions,
            new FixedTimeProvider(Now));
    }

    private static string Catalog(params string[] sources) =>
        $$"""
        {
          "catalogVersion": "1.0",
          "sources": [
        {{string.Join(",\n", sources)}}
          ]
        }
        """;

    private static string Source(
        string sourceId = "G1-TR-ANNUAL",
        string displayName = "Dönem 1 Türkçe yıllık program",
        int classYear = 1) =>
        $$"""
            {
              "sourceId": {{JsonSerializer.Serialize(sourceId)}},
              "displayName": {{JsonSerializer.Serialize(displayName)}},
              "transport": "googleSheets",
              "documentFormat": "googleSheet",
              "sourceUri": "https://docs.google.com/spreadsheets/d/1abc/edit?gid=1",
              "externalId": "1abc",
              "sheetGid": 1,
              "parserProfile": {{JsonSerializer.Serialize("profile_" + sourceId)}},
              "parserProfileVersion": "1.0.0",
              "academicYear": "2026-2027",
              "classYear": {{classYear}},
              "programLanguage": "turkish",
              "timeZoneId": "Europe/Istanbul"
            }
        """;

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>An in-memory catalog file, so the editing rules are tested without a disk.</summary>
    private sealed class FakeCatalogFile(string content) : IScheduleSourceCatalogFile
    {
        public (string Text, string Hash) Content { get; private set; } =
            (content, ScheduleSourceCatalogPlanner.Hash(content));

        public string Path => "/srv/sirkadiyen/shared/config/schedule-sources.json";

        public Task<ScheduleSourceCatalogFileContent> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ScheduleSourceCatalogFileContent
            {
                Content = Content.Text,
                ContentHash = Content.Hash,
                LastModifiedUtc = Now,
                Exists = true,
            });

        public Task<bool> IsWritableAsync(CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task WriteAsync(string content, CancellationToken cancellationToken)
        {
            Content = (content, ScheduleSourceCatalogPlanner.Hash(content));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRevisionStore : IScheduleSourceCatalogRevisionStore
    {
        public List<ScheduleSourceCatalogCommit> Commits { get; } = [];

        public bool Fail { get; set; }

        public Task<int> CommitAsync(
            ScheduleSourceCatalogCommit commit,
            CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new InvalidOperationException("Commit failed.");
            }

            Commits.Add(commit);
            return Task.FromResult(commit.Sources.Count);
        }

        public Task<IReadOnlyList<ScheduleSourceCatalogRevisionSummary>> ListAsync(
            int limit,
            string currentContentHash,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ScheduleSourceCatalogRevisionSummary>>([]);

        public Task<ScheduleSourceCatalogRevisionDetail?> FindAsync(
            Guid id,
            string currentContentHash,
            CancellationToken cancellationToken) =>
            Task.FromResult<ScheduleSourceCatalogRevisionDetail?>(null);
    }
}
