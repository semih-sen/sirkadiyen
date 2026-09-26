using System.Globalization;
using System.Text;
using Sirkadiyen.Application.Common;

namespace Sirkadiyen.Application.Vault;

/// <summary>
/// Runs one vault job end to end. A note job catalogs the vault, lets the agent write the note, uploads
/// it, then lets the agent add backlinks to the existing notes it named; a flashcard job (ADR-170) lets
/// the agent add Spaced Repetition cards to one existing note and writes it back. The agent only ever
/// sees a private workspace directory; every read from and write to the vault is made here, after
/// validation.
/// </summary>
/// <remarks>
/// Once the note has been written by the agent it is not thrown away over a bad proposal: an
/// unparseable answer, unusable title, or unknown folder degrades to a fallback placement with a
/// warning. Only a failure that leaves nothing to upload fails the job.
/// </remarks>
public sealed class VaultNoteJobService(
    IVaultStore store,
    IVaultAgentRunner agent,
    VaultJobRegistry jobs,
    VaultNoteOptions options,
    TimeProvider timeProvider)
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Deletes every workspace left under the root. Called once before the first job runs: any
    /// workspace present then belongs to a job a crash or restart interrupted, and nothing will
    /// resume it.
    /// </summary>
    public void SweepWorkspaceRoot()
    {
        Directory.CreateDirectory(options.WorkspaceRoot);
        foreach (string directory in Directory.EnumerateDirectories(options.WorkspaceRoot))
        {
            TryDeleteDirectory(directory);
        }
    }

    public async Task RunAsync(Guid jobId, CancellationToken cancellationToken)
    {
        // Only a queued job is run. The same id can be queued twice - once on submission and once by
        // the startup recovery that raced it - and the second read finds it already finished.
        VaultJobRecord? job = await jobs.FindRecordAsync(jobId, cancellationToken);
        if (job?.View.Status != VaultJobStatus.Queued)
        {
            return;
        }

        string workspace = Path.Combine(options.WorkspaceRoot, jobId.ToString("N"));
        try
        {
            Directory.CreateDirectory(workspace);
            switch (job.Request)
            {
                case VaultNoteRequest note:
                    await WriteNoteAsync(jobId, note, workspace, cancellationToken);
                    break;
                case VaultFlashcardRequest flashcards:
                    await AddFlashcardsAsync(jobId, flashcards, workspace, cancellationToken);
                    break;
                default:
                    await FailAsync(jobId, $"Bilinmeyen iş türü: {job.Request.Kind}.");
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await FailAsync(jobId, "İş, sunucu kapanırken yarıda kaldı.");
        }
        catch (Exception exception)
        {
            // A job must end in a reported state whatever went wrong inside it; the status board is
            // the only place its requester looks.
            await FailAsync(jobId, ExceptionSummary.Describe(exception));
        }
        finally
        {
            TryDeleteDirectory(workspace);
        }
    }

    private async Task WriteNoteAsync(Guid jobId, VaultNoteRequest request, string workspace, CancellationToken cancellationToken)
    {
        await SetStatusAsync(jobId, VaultJobStatus.Cataloging);
        VaultCatalog catalog = VaultCatalog.Build(await store.ListPathsAsync(cancellationToken));

        await SetStatusAsync(jobId, VaultJobStatus.Generating);
        VaultAgentResult noteRun = await agent.RunAsync(
            workspace,
            VaultPromptBuilder.BuildNotePrompt(request, catalog, options.MaxBacklinks),
            VaultPromptBuilder.NoteOutputSchema,
            cancellationToken);
        if (!noteRun.Succeeded)
        {
            await FailAsync(jobId, $"Not üretilemedi: {noteRun.Failure}");
            return;
        }

        string notePath = Path.Combine(workspace, VaultPromptBuilder.NoteFileName);
        string? noteContent = File.Exists(notePath)
            ? await File.ReadAllTextAsync(notePath, cancellationToken)
            : null;
        if (string.IsNullOrWhiteSpace(noteContent))
        {
            await FailAsync(jobId, $"Agent '{VaultPromptBuilder.NoteFileName}' dosyasını yazmadı.");
            return;
        }

        VaultLinkNormalization links = VaultLinkNormalizer.Normalize(noteContent, catalog);
        noteContent = links.Content;
        if (links.Removed.Count > 0)
        {
            await AddWarningAsync(jobId, $"Vault'ta karşılığı olmayan bağlantılar düz metne çevrildi: {string.Join(", ", links.Removed.Distinct(StringComparer.Ordinal))}");
        }

        // A note whose flashcards the plugin would misfile is still worth keeping: the text is the
        // note, and the cards can be fixed in Obsidian or added again from the panel.
        noteContent = VaultFlashcards.SeparateDeckTag(noteContent);
        foreach (string problem in VaultFlashcards.Problems(noteContent))
        {
            await AddWarningAsync(jobId, $"Flashcard: {problem}");
        }

        VaultNoteDraft? draft = VaultAgentOutputParser.ParseNoteDraft(noteRun.Output, out string? parseError);
        if (draft is null)
        {
            await AddWarningAsync(jobId, $"{parseError} Not varsayılan konuma yazıldı, backlink eklenmedi.");
        }

        VaultNotePlacement placement = await PlaceAsync(jobId, request, draft, catalog);

        await SetStatusAsync(jobId, VaultJobStatus.Uploading);
        VaultWriteOutcome created = await store.CreateAsync(placement.Path, noteContent, cancellationToken);
        if (created == VaultWriteOutcome.Conflict)
        {
            // The catalog was read moments ago and the title made unique against it, so this is a
            // note created in between - rare enough that reporting it beats guessing a new name.
            await FailAsync(jobId, $"'{placement.Path}' bu sırada vault'ta oluşturulmuş; üzerine yazılmadı.");
            return;
        }

        VaultFlashcardSummary flashcards = VaultFlashcards.Summarize(noteContent);
        await jobs.UpdateAsync(
            jobId,
            view => view with { NotePath = placement.Path, NoteLink = placement.Title, Flashcards = flashcards },
            CancellationToken.None);

        if (draft is { Backlinks.Count: > 0 })
        {
            await SetStatusAsync(jobId, VaultJobStatus.Backlinking);
            IReadOnlyList<VaultBacklinkOutcome> outcomes = await AddBacklinksAsync(
                workspace, placement.Title, draft.Backlinks, catalog, cancellationToken);
            await jobs.UpdateAsync(jobId, view => view with { Backlinks = outcomes }, CancellationToken.None);
        }

        await SetStatusAsync(jobId, VaultJobStatus.Succeeded);
    }

    /// <summary>
    /// Adds flashcards to an existing note (ADR-170): the agent edits a copy of the whole note, the
    /// edit is checked to have only added a deck tag, highlights and a card section, and it replaces
    /// the note only if the note is still the version that was read.
    /// </summary>
    private async Task AddFlashcardsAsync(Guid jobId, VaultFlashcardRequest request, string workspace, CancellationToken cancellationToken)
    {
        await SetStatusAsync(jobId, VaultJobStatus.Cataloging);
        VaultCatalog catalog = VaultCatalog.Build(await store.ListPathsAsync(cancellationToken));
        VaultNote? note = catalog.Notes.FirstOrDefault(item => string.Equals(item.Path, request.NotePath, StringComparison.Ordinal));
        VaultDocument? original = note is null ? null : await store.GetAsync(note.Path, cancellationToken);
        if (note is null || original is null)
        {
            await FailAsync(jobId, $"'{request.NotePath}' vault'ta yok.");
            return;
        }

        await jobs.UpdateAsync(jobId, view => view with { NoteLink = note.LinkTarget }, CancellationToken.None);

        // Converting twice would highlight highlighted text and append a second card section; a note
        // that already files cards is left for the user to extend in Obsidian.
        if (VaultFlashcards.Summarize(original.Content).Deck is { } existingDeck)
        {
            await FailAsync(jobId, $"Not zaten flashcard içeriyor ({existingDeck}); yeniden dönüştürülmedi.");
            return;
        }

        string copyPath = Path.Combine(workspace, VaultPromptBuilder.NoteFileName);
        await File.WriteAllTextAsync(copyPath, original.Content, Utf8WithoutBom, cancellationToken);

        await SetStatusAsync(jobId, VaultJobStatus.Generating);
        VaultAgentResult run = await agent.RunAsync(
            workspace,
            VaultPromptBuilder.BuildFlashcardPrompt(note, VaultFlashcards.DeckForFolder(VaultPathPolicy.FolderOf(note.Path))),
            outputSchema: null,
            cancellationToken);
        if (!run.Succeeded)
        {
            await FailAsync(jobId, $"Flashcard'lar eklenemedi: {run.Failure}");
            return;
        }

        string edited = VaultFlashcards.SeparateDeckTag(await File.ReadAllTextAsync(copyPath, cancellationToken));
        if (VaultFlashcards.EvaluateConversion(original.Content, edited) is { } refusal)
        {
            await FailAsync(jobId, $"Agent'ın düzenlemesi reddedildi, not değiştirilmedi: {refusal}");
            return;
        }

        await SetStatusAsync(jobId, VaultJobStatus.Uploading);
        VaultWriteOutcome written = await store.ReplaceAsync(note.Path, edited, original.ETag, cancellationToken);
        if (written == VaultWriteOutcome.Conflict)
        {
            await FailAsync(jobId, "Not bu sırada Obsidian'da değiştirilmiş; yeni hali korundu. Yeniden deneyin.");
            return;
        }

        VaultFlashcardSummary flashcards = VaultFlashcards.Summarize(edited);
        await jobs.UpdateAsync(
            jobId,
            view => view with { Status = VaultJobStatus.Succeeded, NotePath = note.Path, Flashcards = flashcards },
            CancellationToken.None);
    }

    private async Task<VaultNotePlacement> PlaceAsync(Guid jobId, VaultNoteRequest request, VaultNoteDraft? draft, VaultCatalog catalog)
    {
        string? title = request.Title ?? VaultPathPolicy.SanitizeTitle(draft?.Title);
        if (title is null)
        {
            title = "Not " + timeProvider.GetUtcNow().ToString("yyyy-MM-dd HHmm", CultureInfo.InvariantCulture);
            await AddWarningAsync(jobId, $"Agent kullanılabilir bir not adı önermedi; '{title}' kullanıldı.");
        }

        string? folder = request.Folder;
        bool folderMustExist = false;
        if (folder is null && draft?.Folder is { } proposed)
        {
            folder = VaultPathPolicy.NormalizeFolder(proposed, out _);
            folderMustExist = true;
            if (folder is null)
            {
                await AddWarningAsync(jobId, $"Önerilen '{proposed}' klasörü geçersiz; not köke yazıldı.");
            }
        }

        VaultNotePlacement placement = VaultPathPolicy.Place(catalog, folder, folderMustExist, title);
        if (placement.Warning is not null)
        {
            await AddWarningAsync(jobId, placement.Warning);
        }

        if (!string.Equals(placement.Title, title, StringComparison.Ordinal))
        {
            await AddWarningAsync(jobId, $"'{title}' adlı bir not zaten var; yeni not '{placement.Title}' olarak kaydedildi.");
        }

        return placement;
    }

    private async Task<IReadOnlyList<VaultBacklinkOutcome>> AddBacklinksAsync(
        string workspace,
        string newNoteLink,
        IReadOnlyList<VaultBacklinkRequest> requests,
        VaultCatalog catalog,
        CancellationToken cancellationToken)
    {
        List<VaultBacklinkOutcome> outcomes = [];
        List<(VaultNote Note, string? Reason)> accepted = [];
        foreach (VaultBacklinkRequest backlink in requests)
        {
            VaultNote? note = catalog.Find(backlink.Target);
            if (note is null)
            {
                outcomes.Add(new(backlink.Target, null, VaultBacklinkStatus.SkippedInvalid, "Vault'ta böyle bir not yok."));
            }
            else if (accepted.Any(item => item.Note.Path == note.Path))
            {
                continue;
            }
            else if (accepted.Count >= options.MaxBacklinks)
            {
                outcomes.Add(new(backlink.Target, note.Path, VaultBacklinkStatus.SkippedLimit, $"İş başına en fazla {options.MaxBacklinks} not güncellenir."));
            }
            else
            {
                accepted.Add((note, backlink.Reason));
            }
        }

        string backlinkDirectory = Path.Combine(workspace, VaultPromptBuilder.BacklinkFolderName);
        Directory.CreateDirectory(backlinkDirectory);

        // The copies are named by position rather than title: a vault key may hold characters the
        // server's file system does not, and the prompt tells the agent which copy is which note.
        List<(VaultBacklinkTarget Target, VaultDocument Original)> prepared = [];
        foreach ((VaultNote note, string? reason) in accepted)
        {
            VaultDocument? original = await store.GetAsync(note.Path, cancellationToken);
            if (original is null)
            {
                outcomes.Add(new(note.LinkTarget, note.Path, VaultBacklinkStatus.SkippedChanged, "Not artık vault'ta yok."));
                continue;
            }

            string fileName = (prepared.Count + 1).ToString("00", CultureInfo.InvariantCulture) + VaultPathPolicy.NoteExtension;
            string workspaceFile = $"{VaultPromptBuilder.BacklinkFolderName}/{fileName}";
            await File.WriteAllTextAsync(
                Path.Combine(backlinkDirectory, fileName), original.Content, Utf8WithoutBom, cancellationToken);
            prepared.Add((new VaultBacklinkTarget(note, workspaceFile, reason), original));
        }

        if (prepared.Count == 0)
        {
            return outcomes;
        }

        VaultAgentResult run = await agent.RunAsync(
            workspace,
            VaultPromptBuilder.BuildBacklinkPrompt(newNoteLink, prepared.Select(static item => item.Target).ToList()),
            outputSchema: null,
            cancellationToken);
        if (!run.Succeeded)
        {
            outcomes.AddRange(prepared.Select(item => new VaultBacklinkOutcome(
                item.Target.Note.LinkTarget, item.Target.Note.Path, VaultBacklinkStatus.Failed, run.Failure)));
            return outcomes;
        }

        foreach ((VaultBacklinkTarget target, VaultDocument original) in prepared)
        {
            outcomes.Add(await WriteBackAsync(workspace, newNoteLink, target, original, cancellationToken));
        }

        return outcomes;
    }

    private async Task<VaultBacklinkOutcome> WriteBackAsync(
        string workspace,
        string newNoteLink,
        VaultBacklinkTarget target,
        VaultDocument original,
        CancellationToken cancellationToken)
    {
        VaultNote note = target.Note;
        string edited = await File.ReadAllTextAsync(Path.Combine(workspace, target.WorkspaceFile), cancellationToken);
        string? refusal = VaultBacklinkEditCheck.Evaluate(original.Content, edited, newNoteLink);
        if (refusal is not null)
        {
            return new(note.LinkTarget, note.Path, VaultBacklinkStatus.SkippedInvalid, refusal);
        }

        VaultWriteOutcome written = await store.ReplaceAsync(note.Path, edited, original.ETag, cancellationToken);
        return written == VaultWriteOutcome.Written
            ? new(note.LinkTarget, note.Path, VaultBacklinkStatus.Updated, null)
            : new(note.LinkTarget, note.Path, VaultBacklinkStatus.SkippedChanged, "Not bu sırada değiştirilmiş; yeni hali korundu.");
    }

    // Progress is saved without the job's token: a job cancelled by shutdown must still record that
    // it failed, and one whose note is already uploaded must still record where.
    private Task SetStatusAsync(Guid jobId, VaultJobStatus status) =>
        jobs.UpdateAsync(jobId, view => view with { Status = status }, CancellationToken.None);

    private Task FailAsync(Guid jobId, string error) =>
        jobs.UpdateAsync(jobId, view => view with { Status = VaultJobStatus.Failed, Error = error }, CancellationToken.None);

    private Task AddWarningAsync(Guid jobId, string warning) =>
        jobs.UpdateAsync(jobId, view => view with { Warnings = [.. view.Warnings, warning] }, CancellationToken.None);

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A file still held open is left for the next startup sweep; failing the job over its
            // own cleanup would report a written note as lost.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
