using Sirkadiyen.Application.Vault;
using Sirkadiyen.Infrastructure.Persistence;
using Sirkadiyen.Infrastructure.Persistence.Vault;
using Xunit;

namespace Sirkadiyen.Persistence.Tests;

[Collection(PostgresCollection.Name)]
public sealed class VaultJobStoreTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RoundTripsARequestAndItsOutcome()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        VaultNoteRequest request = VaultNoteRequest.Create("Beta blokerler", "", "Beta Blokerler", out _)!;
        VaultJobView queued = new() { Id = Guid.NewGuid(), Status = VaultJobStatus.Queued, CreatedAtUtc = Now };

        await using (SirkadiyenDbContext context = fixture.CreateProductionLikeContext())
        {
            VaultJobStore store = new(context);
            await store.AddAsync(new VaultJobRecord(queued, request, new VaultJobOrigin(VaultJobSource.Admin, "admin@example.com")), Token);
            await store.UpdateAsync(queued.Id, static view => view with { Status = VaultJobStatus.Generating }, Token);
            await store.UpdateAsync(
                queued.Id,
                static view => view with
                {
                    Status = VaultJobStatus.Succeeded,
                    CompletedAtUtc = Now.AddMinutes(1),
                    NotePath = "Beta Blokerler.md",
                    NoteLink = "Beta Blokerler",
                    Backlinks = [new VaultBacklinkOutcome("Hipertansiyon", "Kardiyoloji/Hipertansiyon.md", VaultBacklinkStatus.Updated, null)],
                    Warnings = ["uyarı"],
                },
                Token);
        }

        await using SirkadiyenDbContext reader = fixture.CreateContext();
        VaultJobRecord stored = (await new VaultJobStore(reader).FindAsync(queued.Id, Token))!;

        Assert.Equal(VaultJobStatus.Succeeded, stored.View.Status);
        Assert.Equal(Now.AddMinutes(1), stored.View.CompletedAtUtc);
        Assert.Equal("Beta Blokerler.md", stored.View.NotePath);
        Assert.Equal(
            new VaultBacklinkOutcome("Hipertansiyon", "Kardiyoloji/Hipertansiyon.md", VaultBacklinkStatus.Updated, null),
            Assert.Single(stored.View.Backlinks));
        Assert.Equal(["uyarı"], stored.View.Warnings);
        VaultNoteRequest note = Assert.IsType<VaultNoteRequest>(stored.Request);
        Assert.Equal("Beta blokerler", note.Prompt);
        Assert.Equal(string.Empty, note.Folder);
        Assert.Equal("Beta Blokerler", note.Title);
        Assert.Null(stored.View.Flashcards);
        Assert.Equal(new VaultJobOrigin(VaultJobSource.Admin, "admin@example.com"), stored.Origin);
    }

    [Fact]
    public async Task RoundTripsAFlashcardJobAndListsJobsByNote()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        string path = $"Farmakoloji/{Guid.NewGuid():N}.md";
        VaultFlashcardRequest request = VaultFlashcardRequest.Create(path, out _)!;
        VaultJobView queued = new() { Id = Guid.NewGuid(), Status = VaultJobStatus.Queued, CreatedAtUtc = Now.AddYears(-2) };
        VaultJobView unrelated = new() { Id = Guid.NewGuid(), Status = VaultJobStatus.Queued, CreatedAtUtc = Now.AddYears(-2) };

        await using (SirkadiyenDbContext context = fixture.CreateProductionLikeContext())
        {
            VaultJobStore store = new(context);
            await store.AddAsync(new VaultJobRecord(queued, request, VaultJobOrigin.Shortcut), Token);
            await store.AddAsync(new VaultJobRecord(unrelated, VaultNoteRequest.Create("x", null, null, out _)!, VaultJobOrigin.Shortcut), Token);
            await store.UpdateAsync(
                queued.Id,
                static view => view with
                {
                    Status = VaultJobStatus.Succeeded,
                    Flashcards = new VaultFlashcardSummary("#flashcards/farmakoloji", 7, 4),
                },
                Token);
        }

        await using SirkadiyenDbContext reader = fixture.CreateContext();
        VaultJobStore readerStore = new(reader);
        VaultJobRecord stored = (await readerStore.FindAsync(queued.Id, Token))!;

        Assert.Equal(path, Assert.IsType<VaultFlashcardRequest>(stored.Request).NotePath);
        Assert.Equal(new VaultFlashcardSummary("#flashcards/farmakoloji", 7, 4), stored.View.Flashcards);
        List<Guid> forNotes = [.. (await readerStore.ListForNotesAsync(Token)).Select(static job => job.View.Id)];
        Assert.Contains(queued.Id, forNotes);
        Assert.DoesNotContain(unrelated.Id, forNotes);
    }

    [Fact]
    public async Task ListsUnfinishedOldestFirstAndRecentNewestFirst()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        VaultNoteRequest request = VaultNoteRequest.Create("x", null, null, out _)!;
        DateTimeOffset start = Now.AddYears(1);

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        VaultJobStore store = new(context);
        Guid older = await AddAsync(store, request, start, VaultJobStatus.Queued);
        Guid done = await AddAsync(store, request, start.AddMinutes(1), VaultJobStatus.Failed);
        Guid newer = await AddAsync(store, request, start.AddMinutes(2), VaultJobStatus.Backlinking);

        List<Guid> unfinished = [.. (await store.ListUnfinishedAsync(Token)).Select(static job => job.View.Id)];
        List<Guid> recent = [.. (await store.ListRecentAsync(3, Token)).Select(static job => job.View.Id)];

        Assert.True(unfinished.IndexOf(older) < unfinished.IndexOf(newer));
        Assert.DoesNotContain(done, unfinished);
        Assert.Equal([newer, done, older], recent);
    }

    private static async Task<Guid> AddAsync(VaultJobStore store, VaultNoteRequest request, DateTimeOffset createdAtUtc, VaultJobStatus status)
    {
        VaultJobView view = new() { Id = Guid.NewGuid(), Status = status, CreatedAtUtc = createdAtUtc };
        await store.AddAsync(new VaultJobRecord(view, request, VaultJobOrigin.Shortcut), Token);
        return view.Id;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;
}
