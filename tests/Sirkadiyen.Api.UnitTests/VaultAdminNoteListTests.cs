using Sirkadiyen.Api.Vault;
using Sirkadiyen.Application.Vault;
using Xunit;

namespace Sirkadiyen.Api.UnitTests;

public sealed class VaultAdminNoteListTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Lists_notes_with_their_latest_job_and_recorded_cards()
    {
        VaultObjectInfo[] objects =
        [
            new("Kardiyoloji/Aritmi.md", 120, Now),
            new("Farmakoloji/Beta Blokerler.md", 300, Now),
            new("Farmakoloji/ekg.png", 5000, Now),
            new(".obsidian/app.md", 10, Now),
        ];

        // Newest first: a failed retry on top of the conversion that succeeded.
        VaultJobRecord[] jobs =
        [
            Flashcards("Farmakoloji/Beta Blokerler.md", Now.AddMinutes(2), VaultJobStatus.Failed, null),
            Flashcards("Farmakoloji/Beta Blokerler.md", Now.AddMinutes(1), VaultJobStatus.Succeeded, new("#flashcards/farmakoloji", 6, 5)),
        ];

        IReadOnlyList<VaultAdminNoteResponse> notes = VaultAdminEndpoints.BuildNoteList(objects, jobs);

        Assert.Equal(["Farmakoloji/Beta Blokerler.md", "Kardiyoloji/Aritmi.md"], notes.Select(static note => note.Path));
        VaultAdminNoteResponse beta = notes[0];
        Assert.Equal("Beta Blokerler", beta.Title);
        Assert.Equal("Farmakoloji", beta.Folder);
        Assert.Equal(300, beta.SizeBytes);
        Assert.Equal(VaultJobStatus.Failed, beta.LatestJob?.Status);
        Assert.Equal(VaultJobKind.Flashcards, beta.LatestJob?.Kind);
        Assert.Equal("#flashcards/farmakoloji", beta.Flashcards?.Deck);
        Assert.Equal(6, beta.Flashcards?.ClozeCount);
        Assert.Equal(5, beta.Flashcards?.QuestionCount);
        Assert.Null(notes[1].LatestJob);
        Assert.Null(notes[1].Flashcards);
    }

    [Fact]
    public void Reads_a_note_jobs_path_once_it_has_written_the_note()
    {
        VaultJobRecord queued = new(
            new VaultJobView { Id = Guid.NewGuid(), Status = VaultJobStatus.Generating, CreatedAtUtc = Now },
            VaultNoteRequest.Create("Aritmiler", null, null, out _)!,
            VaultJobOrigin.Shortcut);
        VaultJobRecord written = queued with
        {
            View = queued.View with
            {
                Status = VaultJobStatus.Succeeded,
                NotePath = "Kardiyoloji/Aritmi.md",
                Flashcards = new VaultFlashcardSummary("#flashcards/kardiyoloji", 4, 3),
            },
        };

        Assert.Null(queued.NotePath);

        VaultAdminNoteResponse note = Assert.Single(VaultAdminEndpoints.BuildNoteList(
            [new VaultObjectInfo("Kardiyoloji/Aritmi.md", 120, Now)],
            [written]));
        Assert.Equal(VaultJobKind.Note, note.LatestJob?.Kind);
        Assert.Equal("#flashcards/kardiyoloji", note.Flashcards?.Deck);
    }

    private static VaultJobRecord Flashcards(string path, DateTimeOffset createdAtUtc, VaultJobStatus status, VaultFlashcardSummary? summary) =>
        new(
            new VaultJobView { Id = Guid.NewGuid(), Status = status, CreatedAtUtc = createdAtUtc, Flashcards = summary },
            VaultFlashcardRequest.Create(path, out _)!,
            VaultJobOrigin.Shortcut);
}
