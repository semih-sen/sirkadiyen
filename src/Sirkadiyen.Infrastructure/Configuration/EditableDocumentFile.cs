using System.Text;

namespace Sirkadiyen.Infrastructure.Configuration;

/// <summary>
/// A configuration document on the local file system that the administration panel may read and
/// replace: read it whole, ask whether it can be written, replace it atomically.
/// </summary>
/// <remarks>
/// Shared by the schedule source catalog (ADR-114) and the student roster catalog (ADR-134),
/// because the two file-system rules are the same for both and are the part that is easy to get
/// subtly wrong. What is deliberately not shared is anything above it: each catalog keeps its own
/// port, its own hash and its own validation, so neither can be handed the other's document.
/// <para>
/// A missing file is not an error here. It is exactly what a first deployment to a fresh server
/// looks like, and the editor is the tool for fixing it.
/// </para>
/// </remarks>
internal sealed class EditableDocumentFile(string path)
{
    public string Path => path;

    public bool Exists => File.Exists(path);

    public Task<string> ReadAsync(CancellationToken cancellationToken) =>
        File.Exists(path)
            ? File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken)
            : Task.FromResult(string.Empty);

    public DateTimeOffset? LastModifiedUtc =>
        File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;

    /// <summary>
    /// Whether the directory can be written, so the panel can say "read-only" instead of letting
    /// an operator compose an edit that fails at the last step.
    /// </summary>
    /// <remarks>
    /// A real create-and-delete probe, because the deployed host runs under systemd's
    /// <c>ProtectSystem=strict</c>: the mount is read-only unless the unit lists the path in
    /// <c>ReadWritePaths</c>, and no permission bit on the directory reveals that.
    /// </remarks>
    public bool IsWritable()
    {
        string? directory = System.IO.Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return false;
        }

        string probe = System.IO.Path.Combine(directory, $".sirkadiyen-write-probe-{Guid.NewGuid():N}");
        try
        {
            using (File.Create(probe))
            {
            }

            File.Delete(probe);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Replaces the document atomically: the new content is fully written and flushed to a sibling
    /// temporary file before it is moved into place, so a reader never observes a half-written
    /// document.
    /// </summary>
    public async Task WriteAsync(string content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        string directory = System.IO.Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException(
                $"The configured document path '{path}' has no directory.");
        Directory.CreateDirectory(directory);

        string temporary = System.IO.Path.Combine(
            directory,
            $".{System.IO.Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            // UTF-8 without a byte order mark: the readers of these files and every diff tool
            // expect the same bytes the repository files have always had.
            await using (FileStream stream = new(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            {
                await stream.WriteAsync(new UTF8Encoding(false).GetBytes(content), cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporary, path, overwrite: true);
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
