using Sirkadiyen.Application.StudentRosters;
using Sirkadiyen.Infrastructure.Configuration;

namespace Sirkadiyen.Infrastructure.StudentRosters;

/// <summary>The roster catalog document on the local file system (ADR-134).</summary>
/// <remarks>
/// The path must live outside the API's release directory, for the reason the source catalog's
/// does: a document inside a deployed artifact is replaced by the next deployment, and every
/// administrative edit would silently revert.
/// </remarks>
public sealed class StudentRosterCatalogFile(StudentRosterCatalogFileOptions options)
    : IStudentRosterCatalogFile
{
    private readonly EditableDocumentFile file = new(options.Path);

    public string Path => options.Path;

    public async Task<StudentRosterCatalogFileContent> ReadAsync(
        CancellationToken cancellationToken)
    {
        // The raw text, unparsed: a document that no longer parses must still be readable in the
        // editor, because the editor is where it gets fixed.
        string content = await file.ReadAsync(cancellationToken);
        return new StudentRosterCatalogFileContent
        {
            Content = content,
            ContentHash = StudentRosterCatalogPlanner.Hash(content),
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

/// <summary>Where the editable roster catalog lives.</summary>
public sealed record StudentRosterCatalogFileOptions
{
    public required string Path { get; init; }
}
