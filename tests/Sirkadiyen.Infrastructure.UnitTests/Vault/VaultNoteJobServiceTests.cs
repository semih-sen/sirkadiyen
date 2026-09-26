using Sirkadiyen.Application.Vault;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests.Vault;

public sealed class VaultNoteJobServiceTests : IDisposable
{
    private const string Hypertension = "Kardiyoloji/Hipertansiyon.md";
    private const string HypertensionContent = "# Hipertansiyon\n\nİlk basamak ACE inhibitörleridir.\n";
    private const string Arrhythmia = "Kardiyoloji/Aritmi.md";
    private const string ArrhythmiaContent = "# Aritmi\n\nHız kontrolü önemlidir.\n";

    private static readonly DateTimeOffset Now = new(2026, 9, 23, 10, 0, 0, TimeSpan.Zero);

    private readonly string workspaceRoot = Path.Combine(Path.GetTempPath(), "sirkadiyen-vault-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeVaultStore store = new();
    private readonly ScriptedAgent agent = new();

    public VaultNoteJobServiceTests()
    {
        store.Seed(Hypertension, HypertensionContent);
        store.Seed(Arrhythmia, ArrhythmiaContent);
        store.Seed("Farmakoloji/Giriş.md", "# Giriş\n");
    }

    public void Dispose()
    {
        if (Directory.Exists(workspaceRoot))
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Writes_the_note_and_adds_the_backlinks_it_named()
    {
        agent.Then((workspace, _) =>
        {
            File.WriteAllText(Path.Combine(workspace, "note.md"), "#flashcards/farmakoloji\n\n# Beta Blokerler\n\n[[Hipertansiyon]] ==tedavi==sinde.\n");
            return VaultAgentResult.Success(
                """{"folder":"Farmakoloji","title":"Beta Blokerler","backlinks":[{"target":"Hipertansiyon","reason":"tedavi"},{"target":"Yok"},{"target":"Kardiyoloji/Hipertansiyon"}]}""");
        });
        agent.Then(AppendLinkTo("backlinks/01.md", "Beta Blokerler"));

        (InMemoryVaultJobStore jobs, Guid id) = await RunAsync(Request());

        VaultJobView job = (await jobs.FindAsync(id, CancellationToken.None))!.View;
        Assert.Equal(VaultJobStatus.Succeeded, job.Status);
        Assert.Equal("Farmakoloji/Beta Blokerler.md", job.NotePath);
        Assert.Equal("Beta Blokerler", job.NoteLink);
        Assert.Empty(job.Warnings);
        Assert.Equal(Now, job.CompletedAtUtc);
        Assert.Equal("#flashcards/farmakoloji\n\n# Beta Blokerler\n\n[[Hipertansiyon]] ==tedavi==sinde.\n", store.Content("Farmakoloji/Beta Blokerler.md"));
        Assert.Equal(HypertensionContent + "- [[Beta Blokerler]]\n", store.Content(Hypertension));
        Assert.Equal(
            [
                new VaultBacklinkOutcome("Yok", null, VaultBacklinkStatus.SkippedInvalid, "Vault'ta böyle bir not yok."),
                new VaultBacklinkOutcome("Hipertansiyon", Hypertension, VaultBacklinkStatus.Updated, null),
            ],
            job.Backlinks);

        Assert.Contains("- Hipertansiyon", agent.Prompts[0], StringComparison.Ordinal);
        Assert.Contains("`backlinks/01.md` (not: Hipertansiyon)", agent.Prompts[1], StringComparison.Ordinal);
        Assert.Equal([VaultPromptBuilder.NoteOutputSchema, null], agent.Schemas);
        Assert.Empty(Directory.EnumerateFileSystemEntries(workspaceRoot));
    }

    [Fact]
    public async Task Repairs_the_new_notes_links_before_uploading()
    {
        agent.Then((workspace, _) =>
        {
            File.WriteAllText(Path.Combine(workspace, "note.md"), "[[hipertansiyon]] ve [[Olmayan]].\n");
            return VaultAgentResult.Success("""{"title":"Beta","backlinks":[]}""");
        });

        (InMemoryVaultJobStore jobs, Guid id) = await RunAsync(Request());

        Assert.Equal("[[Hipertansiyon|hipertansiyon]] ve Olmayan.\n", store.Content("Beta.md"));
        Assert.Contains((await jobs.FindAsync(id, CancellationToken.None))!.View.Warnings, static warning => warning.EndsWith(": Olmayan", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Fails_without_writing_when_the_agent_fails()
    {
        agent.Then((_, _) => VaultAgentResult.Failed("zaman aşımı"));

        (InMemoryVaultJobStore jobs, Guid id) = await RunAsync(Request());

        VaultJobView job = (await jobs.FindAsync(id, CancellationToken.None))!.View;
        Assert.Equal(VaultJobStatus.Failed, job.Status);
        Assert.Contains("zaman aşımı", job.Error, StringComparison.Ordinal);
        Assert.Equal(3, store.Paths.Count);
        Assert.Empty(Directory.EnumerateFileSystemEntries(workspaceRoot));
    }

    [Fact]
    public async Task Fails_when_the_agent_wrote_no_note()
    {
        agent.Then((_, _) => VaultAgentResult.Success("""{"title":"X"}"""));

        (InMemoryVaultJobStore jobs, Guid id) = await RunAsync(Request());

        Assert.Equal(VaultJobStatus.Failed, (await jobs.FindAsync(id, CancellationToken.None))!.View.Status);
        Assert.Equal(3, store.Paths.Count);
    }

    [Fact]
    public async Task Keeps_the_note_when_the_answer_is_unreadable()
    {
        agent.Then((workspace, _) =>
        {
            File.WriteAllText(Path.Combine(workspace, "note.md"), "# İçerik\n");
            return VaultAgentResult.Success("Tamamladım.");
        });

        (InMemoryVaultJobStore jobs, Guid id) = await RunAsync(Request());

        VaultJobView job = (await jobs.FindAsync(id, CancellationToken.None))!.View;
        Assert.Equal(VaultJobStatus.Succeeded, job.Status);
        Assert.Equal("Not 2026-09-23 1000.md", job.NotePath);
        Assert.Equal(2, job.Warnings.Count(static warning => !warning.StartsWith("Flashcard:", StringComparison.Ordinal)));
        Assert.Single(agent.Prompts);
    }

    [Fact]
    public async Task Request_overrides_the_proposed_placement()
    {
        agent.Then(WriteNote("""{"folder":"Farmakoloji","title":"Önerilen","backlinks":[]}"""));

        (InMemoryVaultJobStore jobs, Guid id) = await RunAsync(Request(folder: "Yeni Klasör", title: "Aritmi"));

        VaultJobView job = (await jobs.FindAsync(id, CancellationToken.None))!.View;
        Assert.Equal("Yeni Klasör/Aritmi_2.md", job.NotePath);
        Assert.Contains(job.Warnings, static warning => warning.Contains("Aritmi_2", StringComparison.Ordinal));
        Assert.Contains("'Yeni Klasör' klasörüne", agent.Prompts[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Keeps_a_note_changed_while_the_backlink_ran()
    {
        agent.Then(WriteNote("""{"title":"Beta","backlinks":[{"target":"Hipertansiyon"}]}"""));
        agent.Then((workspace, prompt) =>
        {
            store.Seed(Hypertension, "Obsidian'da düzenlendi.\n");
            return AppendLinkTo("backlinks/01.md", "Beta")(workspace, prompt);
        });

        (InMemoryVaultJobStore jobs, Guid id) = await RunAsync(Request());

        Assert.Equal(VaultBacklinkStatus.SkippedChanged, Assert.Single((await jobs.FindAsync(id, CancellationToken.None))!.View.Backlinks).Status);
        Assert.Equal("Obsidian'da düzenlendi.\n", store.Content(Hypertension));
    }

    [Fact]
    public async Task Stops_at_the_backlink_limit()
    {
        agent.Then(WriteNote("""{"title":"Beta","backlinks":[{"target":"Hipertansiyon"},{"target":"Aritmi"}]}"""));
        agent.Then(AppendLinkTo("backlinks/01.md", "Beta"));

        (InMemoryVaultJobStore jobs, Guid id) = await RunAsync(Request(), maxBacklinks: 1);

        VaultJobView job = (await jobs.FindAsync(id, CancellationToken.None))!.View;
        Assert.Equal(VaultBacklinkStatus.SkippedLimit, job.Backlinks.Single(static outcome => outcome.Target == "Aritmi").Status);
        Assert.Equal(VaultBacklinkStatus.Updated, job.Backlinks.Single(static outcome => outcome.Target == "Hipertansiyon").Status);
        Assert.Equal(ArrhythmiaContent, store.Content(Arrhythmia));
    }

    [Fact]
    public async Task Refuses_an_edit_that_rewrote_the_note()
    {
        agent.Then(WriteNote("""{"title":"Beta","backlinks":[{"target":"Hipertansiyon"}]}"""));
        agent.Then((workspace, _) =>
        {
            File.WriteAllText(Path.Combine(workspace, "backlinks", "01.md"), "[[Beta]]\n");
            return VaultAgentResult.Success("tamam");
        });

        (InMemoryVaultJobStore jobs, Guid id) = await RunAsync(Request());

        Assert.Equal(VaultBacklinkStatus.SkippedInvalid, Assert.Single((await jobs.FindAsync(id, CancellationToken.None))!.View.Backlinks).Status);
        Assert.Equal(HypertensionContent, store.Content(Hypertension));
    }

    [Fact]
    public async Task Keeps_the_note_when_the_backlink_run_fails()
    {
        agent.Then(WriteNote("""{"title":"Beta","backlinks":[{"target":"Hipertansiyon"}]}"""));
        agent.Then((_, _) => VaultAgentResult.Failed("kullanım limiti"));

        (InMemoryVaultJobStore jobs, Guid id) = await RunAsync(Request());

        VaultJobView job = (await jobs.FindAsync(id, CancellationToken.None))!.View;
        Assert.Equal(VaultJobStatus.Succeeded, job.Status);
        Assert.Equal("Beta.md", job.NotePath);
        Assert.Equal(
            new VaultBacklinkOutcome("Hipertansiyon", Hypertension, VaultBacklinkStatus.Failed, "kullanım limiti"),
            Assert.Single(job.Backlinks));
        Assert.Equal(HypertensionContent, store.Content(Hypertension));
    }

    [Fact]
    public void Sweep_removes_leftover_workspaces()
    {
        string leftover = Path.Combine(workspaceRoot, "eski-is");
        Directory.CreateDirectory(leftover);
        File.WriteAllText(Path.Combine(leftover, "note.md"), "x");

        CreateService(new InMemoryVaultJobStore(), 5).SweepWorkspaceRoot();

        Assert.False(Directory.Exists(leftover));
        Assert.True(Directory.Exists(workspaceRoot));
    }

    [Fact]
    public async Task Runs_a_job_only_once()
    {
        agent.Then(WriteNote("""{"title":"Beta","backlinks":[]}"""));

        (InMemoryVaultJobStore jobs, Guid id) = await RunAsync(Request());
        await CreateService(jobs, 5).RunAsync(id, CancellationToken.None);

        Assert.Single(agent.Prompts);
        Assert.Equal(VaultJobStatus.Succeeded, (await jobs.FindAsync(id, CancellationToken.None))!.View.Status);
    }

    [Fact]
    public async Task Records_the_new_notes_flashcards()
    {
        agent.Then((workspace, _) =>
        {
            File.WriteAllText(
                Path.Combine(workspace, "note.md"),
                "#flashcards/farmakoloji\n# Beta\n\nBeta blokerler ==astım==da kontrendikedir.\n\n## Flashcards\n\nKontrendike hastalık?::Astım\n");
            return VaultAgentResult.Success("""{"folder":"Farmakoloji","title":"Beta","backlinks":[]}""");
        });

        (InMemoryVaultJobStore jobs, Guid id) = await RunAsync(Request());

        VaultJobView job = (await jobs.FindAsync(id, CancellationToken.None))!.View;
        Assert.Equal(new VaultFlashcardSummary("#flashcards/farmakoloji", 1, 1), job.Flashcards);
        Assert.Empty(job.Warnings);
        Assert.StartsWith("#flashcards/farmakoloji\n\n# Beta", store.Content("Farmakoloji/Beta.md"), StringComparison.Ordinal);
        Assert.Contains("Flashcard'lar", agent.Prompts[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Warns_when_the_new_note_has_no_flashcards()
    {
        agent.Then(WriteNote("""{"title":"Beta","backlinks":[]}"""));

        (InMemoryVaultJobStore jobs, Guid id) = await RunAsync(Request());

        VaultJobView job = (await jobs.FindAsync(id, CancellationToken.None))!.View;
        Assert.Equal(VaultJobStatus.Succeeded, job.Status);
        Assert.Equal(2, job.Warnings.Count(static warning => warning.StartsWith("Flashcard:", StringComparison.Ordinal)));
        Assert.Equal(new VaultFlashcardSummary(null, 0, 0), job.Flashcards);
    }

    [Fact]
    public async Task Adds_flashcards_to_an_existing_note()
    {
        agent.Then((workspace, prompt) =>
        {
            string copy = Path.Combine(workspace, "note.md");
            Assert.Equal(HypertensionContent, File.ReadAllText(copy));
            File.WriteAllText(
                copy,
                "#flashcards/kardiyoloji\n# Hipertansiyon\n\nİlk basamak ==ACE inhibitörleri==dir.\n\n## Flashcards\n\nİlk basamak ilaç grubu?::ACE inhibitörleri\n");
            return VaultAgentResult.Success("tamam");
        });

        (InMemoryVaultJobStore jobs, Guid id) = await RunAsync(FlashcardRequest(Hypertension));

        VaultJobView job = (await jobs.FindAsync(id, CancellationToken.None))!.View;
        Assert.Equal(VaultJobStatus.Succeeded, job.Status);
        Assert.Equal(Hypertension, job.NotePath);
        Assert.Equal("Hipertansiyon", job.NoteLink);
        Assert.Equal(new VaultFlashcardSummary("#flashcards/kardiyoloji", 1, 1), job.Flashcards);
        Assert.StartsWith("#flashcards/kardiyoloji\n\n# Hipertansiyon", store.Content(Hypertension), StringComparison.Ordinal);
        Assert.Contains("`#flashcards/kardiyoloji`", Assert.Single(agent.Prompts), StringComparison.Ordinal);
        Assert.Equal([null], agent.Schemas);
        Assert.Empty(Directory.EnumerateFileSystemEntries(workspaceRoot));
    }

    [Fact]
    public async Task Keeps_the_note_when_the_conversion_rewrote_it()
    {
        agent.Then((workspace, _) =>
        {
            File.WriteAllText(Path.Combine(workspace, "note.md"), "#flashcards/kardiyoloji\n\n# HT\n\nTedavide ==ACEi== kullanılır.\n");
            return VaultAgentResult.Success("tamam");
        });

        (InMemoryVaultJobStore jobs, Guid id) = await RunAsync(FlashcardRequest(Hypertension));

        VaultJobView job = (await jobs.FindAsync(id, CancellationToken.None))!.View;
        Assert.Equal(VaultJobStatus.Failed, job.Status);
        Assert.Contains("reddedildi", job.Error, StringComparison.Ordinal);
        Assert.Equal(HypertensionContent, store.Content(Hypertension));
    }

    [Fact]
    public async Task Keeps_a_note_changed_while_its_flashcards_were_written()
    {
        agent.Then((workspace, _) =>
        {
            store.Seed(Hypertension, "Obsidian'da düzenlendi.\n");
            File.WriteAllText(
                Path.Combine(workspace, "note.md"),
                "#flashcards/kardiyoloji\n\n# Hipertansiyon\n\nİlk basamak ==ACE inhibitörleri==dir.\n");
            return VaultAgentResult.Success("tamam");
        });

        (InMemoryVaultJobStore jobs, Guid id) = await RunAsync(FlashcardRequest(Hypertension));

        VaultJobView job = (await jobs.FindAsync(id, CancellationToken.None))!.View;
        Assert.Equal(VaultJobStatus.Failed, job.Status);
        Assert.Equal("Obsidian'da düzenlendi.\n", store.Content(Hypertension));
    }

    [Fact]
    public async Task Does_not_convert_a_note_twice()
    {
        store.Seed(Hypertension, "#flashcards/kardiyoloji\n\n# Hipertansiyon\n\nSoru::Cevap\n");

        (InMemoryVaultJobStore jobs, Guid id) = await RunAsync(FlashcardRequest(Hypertension));

        VaultJobView job = (await jobs.FindAsync(id, CancellationToken.None))!.View;
        Assert.Equal(VaultJobStatus.Failed, job.Status);
        Assert.Contains("zaten flashcard", job.Error, StringComparison.Ordinal);
        Assert.Empty(agent.Prompts);
    }

    [Fact]
    public async Task Fails_a_conversion_of_a_missing_note()
    {
        (InMemoryVaultJobStore jobs, Guid id) = await RunAsync(FlashcardRequest("Yok/Olmayan.md"));

        VaultJobView job = (await jobs.FindAsync(id, CancellationToken.None))!.View;
        Assert.Equal(VaultJobStatus.Failed, job.Status);
        Assert.Contains("vault'ta yok", job.Error, StringComparison.Ordinal);
        Assert.Empty(agent.Prompts);
    }

    private async Task<(InMemoryVaultJobStore Jobs, Guid Id)> RunAsync(VaultJobRequest request, int maxBacklinks = 5)
    {
        InMemoryVaultJobStore jobs = new();
        Guid id = (await Registry(jobs).SubmitAsync(request, VaultJobOrigin.Shortcut, CancellationToken.None)).Id;
        await CreateService(jobs, maxBacklinks).RunAsync(id, CancellationToken.None);
        return (jobs, id);
    }

    private static VaultJobRegistry Registry(InMemoryVaultJobStore jobs) => new(jobs, new VaultJobQueue(), new FixedClock());

    private VaultNoteJobService CreateService(InMemoryVaultJobStore jobs, int maxBacklinks) =>
        new(store, agent, Registry(jobs), Options(maxBacklinks), new FixedClock());

    private VaultNoteOptions Options(int maxBacklinks) => new()
    {
        WorkspaceRoot = workspaceRoot,
        MaxBacklinks = maxBacklinks,
    };

    private static VaultNoteRequest Request(string? folder = null, string? title = null) =>
        VaultNoteRequest.Create("Beta blokerler hakkında özet", folder, title, out _)!;

    private static VaultFlashcardRequest FlashcardRequest(string path) => VaultFlashcardRequest.Create(path, out _)!;

    private static Func<string, string, VaultAgentResult> WriteNote(string answer) => (workspace, _) =>
    {
        File.WriteAllText(Path.Combine(workspace, "note.md"), "# Not\n");
        return VaultAgentResult.Success(answer);
    };

    private static Func<string, string, VaultAgentResult> AppendLinkTo(string file, string link) => (workspace, _) =>
    {
        File.AppendAllText(Path.Combine(workspace, file), $"- [[{link}]]\n");
        return VaultAgentResult.Success("tamam");
    };

    private sealed class ScriptedAgent : IVaultAgentRunner
    {
        private readonly Queue<Func<string, string, VaultAgentResult>> steps = new();

        public List<string> Prompts { get; } = [];

        public List<string?> Schemas { get; } = [];

        public void Then(Func<string, string, VaultAgentResult> step) => steps.Enqueue(step);

        public Task<VaultAgentResult> RunAsync(
            string workingDirectory,
            string prompt,
            string? outputSchema,
            CancellationToken cancellationToken)
        {
            Prompts.Add(prompt);
            Schemas.Add(outputSchema);
            return Task.FromResult(steps.Dequeue()(workingDirectory, prompt));
        }
    }

    private sealed class FakeVaultStore : IVaultStore
    {
        private readonly Dictionary<string, (string Content, int Version)> objects = new(StringComparer.Ordinal);

        public IReadOnlyCollection<string> Paths => objects.Keys;

        public void Seed(string path, string content) =>
            objects[path] = (content, objects.TryGetValue(path, out var existing) ? existing.Version + 1 : 1);

        public string Content(string path) => objects[path].Content;

        public Task<IReadOnlyList<string>> ListPathsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>([.. objects.Keys]);

        public Task<IReadOnlyList<VaultObjectInfo>> ListObjectsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<VaultObjectInfo>>(
                [.. objects.Select(static item => new VaultObjectInfo(item.Key, item.Value.Content.Length, null))]);

        public Task<VaultDocument?> GetAsync(string path, CancellationToken cancellationToken) =>
            Task.FromResult(objects.TryGetValue(path, out var stored)
                ? new VaultDocument(path, stored.Content, stored.Version.ToString(System.Globalization.CultureInfo.InvariantCulture))
                : null);

        public Task<VaultWriteOutcome> CreateAsync(string path, string content, CancellationToken cancellationToken)
        {
            if (objects.ContainsKey(path))
            {
                return Task.FromResult(VaultWriteOutcome.Conflict);
            }

            objects[path] = (content, 1);
            return Task.FromResult(VaultWriteOutcome.Written);
        }

        public Task<VaultWriteOutcome> ReplaceAsync(string path, string content, string expectedETag, CancellationToken cancellationToken)
        {
            if (!objects.TryGetValue(path, out var stored)
                || stored.Version.ToString(System.Globalization.CultureInfo.InvariantCulture) != expectedETag)
            {
                return Task.FromResult(VaultWriteOutcome.Conflict);
            }

            objects[path] = (content, stored.Version + 1);
            return Task.FromResult(VaultWriteOutcome.Written);
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
