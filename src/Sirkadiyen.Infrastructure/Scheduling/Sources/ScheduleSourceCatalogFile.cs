using Sirkadiyen.Application.Scheduling.Sources;
using Sirkadiyen.Infrastructure.Configuration;

namespace Sirkadiyen.Infrastructure.Scheduling.Sources;

/// <summary>The catalog document on the local file system (ADR-114).</summary>
/// <remarks>
/// The path is shared with the worker, which reads the same file at startup, so it must live
/// outside either host's release directory: a catalog inside a deployed artifact is replaced by the
/// next deployment, and every administrative edit would silently revert.
/// <para>
/// The file-system rules — read whole, probe for writability, replace atomically — live in
/// <see cref="EditableDocumentFile"/>, which the student roster catalog uses as well. A partially
/// written catalog would be a worker that refuses to start, and that guarantee is worth having in
/// one place rather than in two.
/// </para>
/// </remarks>
public sealed class ScheduleSourceCatalogFile(ScheduleSourceCatalogFileOptions options)
    : IScheduleSourceCatalogFile
{
    private readonly EditableDocumentFile file = new(options.Path);

    public string Path => options.Path;

    public async Task<ScheduleSourceCatalogFileContent> ReadAsync(
        CancellationToken cancellationToken)
    {
        // The raw text, unparsed, and a missing file read as empty content rather than as a
        // failure: both are states the editor must be able to show and fix, and a missing catalog
        // is exactly what a first deployment to a fresh server looks like.
        string content = await file.ReadAsync(cancellationToken);
        return new ScheduleSourceCatalogFileContent
        {
            Content = content,
            ContentHash = ScheduleSourceCatalogPlanner.Hash(content),
            LastModifiedUtc = file.LastModifiedUtc,
            Exists = file.Exists,
        };
    }

    public Task<bool> IsWritableAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(file.IsWritable());
    }

    public Task WriteAsync(string content, CancellationToken cancellationToken) =>
        file.WriteAsync(content, cancellationToken);
}

/// <summary>Where the editable catalog lives.</summary>
public sealed record ScheduleSourceCatalogFileOptions
{
    public required string Path { get; init; }
}
