using System.Text;
using Sirkadiyen.Application.Scheduling.Sources;

namespace Sirkadiyen.Infrastructure.Scheduling.Sources;

/// <summary>The catalog document on the local file system (ADR-114).</summary>
/// <remarks>
/// The path is shared with the worker, which reads the same file at startup, so it must live
/// outside either host's release directory: a catalog inside a deployed artifact is replaced by the
/// next deployment, and every administrative edit would silently revert.
/// </remarks>
public sealed class ScheduleSourceCatalogFile(ScheduleSourceCatalogFileOptions options)
    : IScheduleSourceCatalogFile
{
    public string Path => options.Path;

    public async Task<ScheduleSourceCatalogFileContent> ReadAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(Path))
        {
            // A missing catalog is a state the editor must be able to show and fix, not a crash:
            // it is exactly what a first deployment to a fresh server looks like.
            return new ScheduleSourceCatalogFileContent
            {
                Content = string.Empty,
                ContentHash = ScheduleSourceCatalogPlanner.Hash(string.Empty),
                LastModifiedUtc = null,
                Exists = false,
            };
        }

        string content = await File.ReadAllTextAsync(Path, Encoding.UTF8, cancellationToken);
        return new ScheduleSourceCatalogFileContent
        {
            Content = content,
            ContentHash = ScheduleSourceCatalogPlanner.Hash(content),
            LastModifiedUtc = File.GetLastWriteTimeUtc(Path),
            Exists = true,
        };
    }

    /// <summary>
    /// Whether the directory can be written, so the panel can say "read-only" instead of letting
    /// an operator compose an edit that fails at the last step.
    /// </summary>
    public Task<bool> IsWritableAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string? directory = System.IO.Path.GetDirectoryName(Path);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return Task.FromResult(false);
        }

        // A real create-and-delete probe, because the deployed host runs under systemd's
        // ProtectSystem=strict: the mount is read-only unless the unit lists the path in
        // ReadWritePaths, and no permission bit on the directory reveals that.
        string probe = System.IO.Path.Combine(directory, $".sirkadiyen-write-probe-{Guid.NewGuid():N}");
        try
        {
            using (File.Create(probe))
            {
            }

            File.Delete(probe);
            return Task.FromResult(true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// Replaces the catalog atomically. The worker reads this file at startup, and a partially
    /// written document is a worker that refuses to start, so the new content is fully written and
    /// flushed to a sibling temporary file before it is moved into place.
    /// </summary>
    public async Task WriteAsync(string content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        string directory = System.IO.Path.GetDirectoryName(Path)
            ?? throw new InvalidOperationException(
                $"The configured catalog path '{Path}' has no directory.");
        Directory.CreateDirectory(directory);

        string temporary = System.IO.Path.Combine(
            directory,
            $".{System.IO.Path.GetFileName(Path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            // UTF-8 without a byte order mark: the worker's reader and every diff tool expect the
            // same bytes the repository file has always had.
            await using (FileStream stream = new(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            {
                await stream.WriteAsync(new UTF8Encoding(false).GetBytes(content), cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporary, Path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}

/// <summary>Where the editable catalog lives.</summary>
public sealed record ScheduleSourceCatalogFileOptions
{
    public required string Path { get; init; }
}
