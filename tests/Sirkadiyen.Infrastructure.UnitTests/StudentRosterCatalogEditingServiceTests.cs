using System.Text.Json;
using Sirkadiyen.Application.StudentRosters;
using Sirkadiyen.Infrastructure.StudentRosters;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>
/// Covers the administrative roster catalog editor (ADR-134). The catalog decides which published
/// list answers for which cohort and what its columns mean, so the rules under test are the ones
/// that stop an edit from filling students' profiles with values nobody intended: the plan hash,
/// the on-disk hash, the refusal of a document the lookup could not load, the file rollback when
/// the commit fails, and the dropped reading that makes an applied edit actually apply.
/// </summary>
public sealed class StudentRosterCatalogEditingServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReadReturnsAnUnparseableDocumentWithItsReasonAsync()
    {
        // The editor is the tool for repairing a broken catalog, so refusing to show one would
        // leave a server shell as the only repair path.
        FakeCatalogFile file = new("{ this is not json");
        StudentRosterCatalogEditingService service = Service(file, out _, out _);

        StudentRosterCatalogDocument document = await service.ReadAsync(CancellationToken.None);

        Assert.False(document.IsValid);
        Assert.NotNull(document.ValidationError);
        Assert.Equal("{ this is not json", document.Content);
        Assert.Null(document.RosterCount);
    }

    [Fact]
    public async Task PreviewClassifiesADisplayNameChangeAsLowRiskAsync()
    {
        FakeCatalogFile file = new(Catalog(Roster()));
        StudentRosterCatalogEditingService service = Service(file, out _, out _);

        StudentRosterCatalogPlan plan = await service.PreviewAsync(
            Catalog(Roster(displayName: "Yeni ad")),
            file.Content.Hash,
            CancellationToken.None);

        StudentRosterCatalogRosterChange change = Assert.Single(plan.Modified);
        StudentRosterCatalogFieldChange field = Assert.Single(change.Fields);
        Assert.Equal("displayName", field.Field);
        Assert.Equal(StudentRosterCatalogChangeRisk.Low, field.Risk);
        Assert.False(plan.HasHighRiskChange);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public async Task PreviewShowsTheWholeValueMapOnBothSidesOfARemappingAsync()
    {
        // The edit that fails silently: nothing about the document, the cohort or the headers
        // changes, and every student in group A is enrolled in group B's practicals. The plan has
        // to state both maps in full, because "the map changed" is not something one can review.
        FakeCatalogFile file = new(Catalog(Roster()));
        StudentRosterCatalogEditingService service = Service(file, out _, out _);

        StudentRosterCatalogPlan plan = await service.PreviewAsync(
            Catalog(Roster(valueMap: """{ "a": "B", "b": "A" }""")),
            file.Content.Hash,
            CancellationToken.None);

        StudentRosterCatalogRosterChange change = Assert.Single(plan.Modified);
        StudentRosterCatalogFieldChange field = Assert.Single(change.Fields);
        Assert.Equal("layout.dimensionColumns[practiceGroup]", field.Field);
        Assert.Contains("a→A", field.Before, StringComparison.Ordinal);
        Assert.Contains("a→B", field.After, StringComparison.Ordinal);
        Assert.True(plan.HasHighRiskChange);
        Assert.Contains(plan.Warnings, warning => warning.Code == "layout-changed");
    }

    [Fact]
    public async Task PreviewFlagsARetargetedCohortAsHighRiskAsync()
    {
        FakeCatalogFile file = new(Catalog(Roster()));
        StudentRosterCatalogEditingService service = Service(file, out _, out _);

        StudentRosterCatalogPlan plan = await service.PreviewAsync(
            Catalog(Roster(classYear: 3)),
            file.Content.Hash,
            CancellationToken.None);

        Assert.True(plan.HasHighRiskChange);
        Assert.Contains(plan.Warnings, warning => warning.Code == "cohort-retargeted");
    }

    [Fact]
    public async Task PreviewRefusesADocumentTheLookupCouldNotLoadAsync()
    {
        // Same loader as the lookup's own path, so an accepted edit can never be a catalog the
        // lookup refuses to read. Two lists for one cohort makes every lookup in it ambiguous.
        FakeCatalogFile file = new(Catalog(Roster()));
        StudentRosterCatalogEditingService service = Service(file, out _, out _);

        StudentRosterCatalogValidationException exception =
            await Assert.ThrowsAsync<StudentRosterCatalogValidationException>(
                () => service.PreviewAsync(
                    Catalog(Roster(), Roster(rosterId: "G2-TR-ROSTER-COPY")),
                    file.Content.Hash,
                    CancellationToken.None));

        Assert.Contains("class year 2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreviewRefusesADocumentWithAnUnknownPropertyAsync()
    {
        // A mistyped property would deserialize to nothing and validate cleanly, leaving the list
        // read exactly as before with no sign the edit did not take.
        FakeCatalogFile file = new(Catalog(Roster()));
        StudentRosterCatalogEditingService service = Service(file, out _, out _);

        string typo = Catalog(Roster()).Replace(
            "\"studentNumberHeader\"",
            "\"studentNumberHeaders\"",
            StringComparison.Ordinal);

        await Assert.ThrowsAsync<StudentRosterCatalogValidationException>(
            () => service.PreviewAsync(typo, file.Content.Hash, CancellationToken.None));
    }

    [Fact]
    public async Task PreviewRefusesAStaleBaseHashAsync()
    {
        FakeCatalogFile file = new(Catalog(Roster()));
        StudentRosterCatalogEditingService service = Service(file, out _, out _);

        await Assert.ThrowsAsync<StudentRosterCatalogConflictException>(
            () => service.PreviewAsync(
                Catalog(Roster(displayName: "Yeni ad")),
                StudentRosterCatalogPlanner.Hash("someone else's catalog"),
                CancellationToken.None));
    }

    [Fact]
    public async Task ApplyWritesTheDocumentAndRecordsTheRevisionAsync()
    {
        FakeCatalogFile file = new(Catalog(Roster()));
        StudentRosterCatalogEditingService service = Service(
            file,
            out FakeRevisionStore revisions,
            out _);
        string proposed = Catalog(Roster(displayName: "Yeni ad"));
        StudentRosterCatalogPlan plan = await service.PreviewAsync(
            proposed,
            file.Content.Hash,
            CancellationToken.None);

        StudentRosterCatalogApplyResult result = await service.ApplyAsync(
            Command(proposed, plan),
            CancellationToken.None);

        Assert.Equal(StudentRosterCatalogPlanner.Normalize(proposed), file.Content.Text);
        Assert.Equal(plan.ProposedContentHash, result.ContentHash);
        StudentRosterCatalogCommit commit = Assert.Single(revisions.Commits);
        Assert.Equal(result.RevisionId, commit.Revision.Id);

        // The first edit also records what was on disk before anyone edited it; without it the
        // oldest restorable state would be the result of the first edit.
        Assert.NotNull(commit.Baseline);
        Assert.Equal(Catalog(Roster()), commit.Baseline.Content);
    }

    [Fact]
    public async Task ApplyDropsTheHeldReadingAsync()
    {
        // Without this the panel reports an applied edit while every lookup keeps answering from
        // the documents the previous catalog named, for up to the refresh interval.
        FakeCatalogFile file = new(Catalog(Roster()));
        StudentRosterCatalogEditingService service = Service(file, out _, out FakeIndex index);
        string proposed = Catalog(Roster(displayName: "Yeni ad"));
        StudentRosterCatalogPlan plan = await service.PreviewAsync(
            proposed,
            file.Content.Hash,
            CancellationToken.None);

        StudentRosterCatalogApplyResult result = await service.ApplyAsync(
            Command(proposed, plan),
            CancellationToken.None);

        Assert.Equal(1, index.Invalidations);
        Assert.True(result.ReadingInvalidated);
    }

    [Fact]
    public async Task ApplyRefusesAConfirmationOfADifferentPlanAsync()
    {
        // The plan hash binds the confirmation to the pair of documents the operator was shown.
        FakeCatalogFile file = new(Catalog(Roster()));
        StudentRosterCatalogEditingService service = Service(file, out _, out _);
        string proposed = Catalog(Roster(displayName: "Yeni ad"));
        StudentRosterCatalogPlan plan = await service.PreviewAsync(
            proposed,
            file.Content.Hash,
            CancellationToken.None);

        // Confirmed with the plan of one edit, but carrying a different document.
        await Assert.ThrowsAsync<StudentRosterCatalogConflictException>(
            () => service.ApplyAsync(
                Command(Catalog(Roster(displayName: "Başka ad")), plan),
                CancellationToken.None));

        Assert.Equal(Catalog(Roster()), file.Content.Text);
    }

    [Fact]
    public async Task ApplyRequiresAReasonAsync()
    {
        FakeCatalogFile file = new(Catalog(Roster()));
        StudentRosterCatalogEditingService service = Service(file, out _, out _);
        string proposed = Catalog(Roster(displayName: "Yeni ad"));
        StudentRosterCatalogPlan plan = await service.PreviewAsync(
            proposed,
            file.Content.Hash,
            CancellationToken.None);

        await Assert.ThrowsAsync<StudentRosterCatalogValidationException>(
            () => service.ApplyAsync(
                Command(proposed, plan) with { Reason = "   " },
                CancellationToken.None));

        Assert.Equal(Catalog(Roster()), file.Content.Text);
    }

    [Fact]
    public async Task ApplyRefusesADocumentThatChangesNothingAsync()
    {
        FakeCatalogFile file = new(Catalog(Roster()));
        StudentRosterCatalogEditingService service = Service(file, out _, out _);

        StudentRosterCatalogValidationException exception =
            await Assert.ThrowsAsync<StudentRosterCatalogValidationException>(
                () => service.ApplyAsync(
                    new StudentRosterCatalogApplyCommand
                    {
                        Content = Catalog(Roster()),
                        BaseContentHash = file.Content.Hash,
                        PlanHash = StudentRosterCatalogPlanner.PlanHash(
                            file.Content.Hash,
                            StudentRosterCatalogPlanner.Hash(
                                StudentRosterCatalogPlanner.Normalize(Catalog(Roster())))),
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
        // A document in force that no revision explains is one nobody can restore from.
        string original = Catalog(Roster());
        FakeCatalogFile file = new(original);
        StudentRosterCatalogEditingService service = Service(
            file,
            out FakeRevisionStore revisions,
            out FakeIndex index);
        revisions.Fail = true;
        string proposed = Catalog(Roster(displayName: "Yeni ad"));
        StudentRosterCatalogPlan plan = await service.PreviewAsync(
            proposed,
            file.Content.Hash,
            CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ApplyAsync(Command(proposed, plan), CancellationToken.None));

        Assert.Equal(original, file.Content.Text);

        // And the reading is not dropped, because nothing was applied.
        Assert.Equal(0, index.Invalidations);
    }

    private static StudentRosterCatalogApplyCommand Command(
        string content,
        StudentRosterCatalogPlan plan) => new()
        {
            Content = content,
            BaseContentHash = plan.BaseContentHash,
            PlanHash = plan.PlanHash,
            Reason = "Liste adı düzeltildi",
            ActorUserId = Guid.NewGuid(),
            ActorEmail = "admin@example.com",
        };

    private static StudentRosterCatalogEditingService Service(
        FakeCatalogFile file,
        out FakeRevisionStore revisions,
        out FakeIndex index)
    {
        revisions = new FakeRevisionStore();
        index = new FakeIndex();
        return new StudentRosterCatalogEditingService(
            file,
            new StudentRosterCatalogLoader(),
            revisions,
            index,
            new FixedTimeProvider(Now));
    }

    private static string Catalog(params string[] rosters) =>
        $$"""
        {
          "catalogVersion": "1.0",
          "rosters": [
        {{string.Join(",\n", rosters)}}
          ]
        }
        """;

    private static string Roster(
        string rosterId = "G2-TR-ROSTER",
        string displayName = "Dönem 2 Türkçe öğrenci listesi",
        int classYear = 2,
        string valueMap = """{ "a": "A", "b": "B" }""") =>
        $$"""
            {
              "rosterId": {{JsonSerializer.Serialize(rosterId)}},
              "displayName": {{JsonSerializer.Serialize(displayName)}},
              "transport": "googleSheets",
              "documentFormat": "googleSheet",
              "sourceUri": "https://docs.google.com/spreadsheets/d/1abc/edit?gid=1",
              "externalId": "1abc",
              "sheetGid": 1,
              "academicYear": "2026-2027",
              "classYear": {{classYear}},
              "programLanguage": "turkish",
              "layout": {
                "worksheetTitle": "Sayfa1",
                "headerRow": 1,
                "studentNumberHeader": "Öğrenci No",
                "givenNameHeader": "Ad",
                "familyNameHeader": "Soyad",
                "dimensionColumns": [
                  {
                    "header": "GRUP",
                    "dimension": "practiceGroup",
                    "statedOncePerMergedRun": true,
                    "valueMap": {{valueMap}}
                  }
                ]
              }
            }
        """;

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>An in-memory catalog file, so the editing rules are tested without a disk.</summary>
    private sealed class FakeCatalogFile(string content) : IStudentRosterCatalogFile
    {
        public (string Text, string Hash) Content { get; private set; } =
            (content, StudentRosterCatalogPlanner.Hash(content));

        public string Path => "/srv/sirkadiyen/shared/config/student-rosters.json";

        public Task<StudentRosterCatalogFileContent> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new StudentRosterCatalogFileContent
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
            Content = (content, StudentRosterCatalogPlanner.Hash(content));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRevisionStore : IStudentRosterCatalogRevisionStore
    {
        public List<StudentRosterCatalogCommit> Commits { get; } = [];

        public bool Fail { get; set; }

        public Task CommitAsync(
            StudentRosterCatalogCommit commit,
            CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new InvalidOperationException("Commit failed.");
            }

            Commits.Add(commit);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<StudentRosterCatalogRevisionSummary>> ListAsync(
            int limit,
            string currentContentHash,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<StudentRosterCatalogRevisionSummary>>([]);

        public Task<StudentRosterCatalogRevisionDetail?> FindAsync(
            Guid id,
            string currentContentHash,
            CancellationToken cancellationToken) =>
            Task.FromResult<StudentRosterCatalogRevisionDetail?>(null);
    }

    private sealed class FakeIndex : IStudentRosterIndex
    {
        public int Invalidations { get; private set; }

        public Task<StudentRosterIndexSnapshot> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new StudentRosterIndexSnapshot { ReadAtUtc = Now });

        public void Invalidate() => Invalidations++;
    }
}
