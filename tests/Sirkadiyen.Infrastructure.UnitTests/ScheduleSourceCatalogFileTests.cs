using System.Text;
using Sirkadiyen.Application.Scheduling.Sources;
using Sirkadiyen.Infrastructure.Scheduling.Sources;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>
/// Covers the on-disk half of the catalog editor (ADR-114): the worker reads this file at startup,
/// so a half-written document is a worker that will not start.
/// </summary>
public sealed class ScheduleSourceCatalogFileTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"sirkadiyen-catalog-{Guid.NewGuid():N}");

    [Fact]
    public async Task AMissingCatalogReadsAsEmptyRatherThanThrowingAsync()
    {
        // What a first deployment to a fresh server looks like. The editor has to be able to
        // show it and write the first document.
        ScheduleSourceCatalogFile file = File();

        ScheduleSourceCatalogFileContent content = await file.ReadAsync(CancellationToken.None);

        Assert.False(content.Exists);
        Assert.Equal(string.Empty, content.Content);
        Assert.Null(content.LastModifiedUtc);
    }

    [Fact]
    public async Task TheContentHashIsTheHashOfWhatWasWrittenAsync()
    {
        ScheduleSourceCatalogFile file = File();
        await file.WriteAsync("{\n  \"catalogVersion\": \"1.0\"\n}\n", CancellationToken.None);

        ScheduleSourceCatalogFileContent content = await file.ReadAsync(CancellationToken.None);

        Assert.True(content.Exists);
        Assert.Equal(
            ScheduleSourceCatalogPlanner.Hash(content.Content),
            content.ContentHash);
    }

    [Fact]
    public async Task TheDocumentIsWrittenWithoutAByteOrderMarkAsync()
    {
        // The worker's reader and every diff tool expect the same bytes the repository file has
        // always had; a BOM would change the hash of an otherwise identical document.
        ScheduleSourceCatalogFile file = File();
        await file.WriteAsync("{}\n", CancellationToken.None);

        byte[] bytes = await System.IO.File.ReadAllBytesAsync(file.Path);

        Assert.Equal(Encoding.UTF8.GetBytes("{}\n"), bytes);
    }

    [Fact]
    public async Task WritingLeavesNoTemporaryFileBehindAsync()
    {
        ScheduleSourceCatalogFile file = File();
        await file.WriteAsync("{}\n", CancellationToken.None);
        await file.WriteAsync("{ \"catalogVersion\": \"1.0\" }\n", CancellationToken.None);

        Assert.Equal(
            [Path.GetFileName(file.Path)],
            Directory.GetFiles(directory).Select(Path.GetFileName));
    }

    [Fact]
    public async Task AWritableDirectoryIsReportedWritableAsync()
    {
        ScheduleSourceCatalogFile file = File();
        Directory.CreateDirectory(directory);

        Assert.True(await file.IsWritableAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AMissingDirectoryIsNotWritableAsync()
    {
        // The panel says "read-only" instead of letting an operator compose an edit that fails at
        // the last step: on the deployed host this is what a unit without ReadWritePaths looks like.
        ScheduleSourceCatalogFile file = File();

        Assert.False(await file.IsWritableAsync(CancellationToken.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private ScheduleSourceCatalogFile File() => new(new ScheduleSourceCatalogFileOptions
    {
        Path = Path.Combine(directory, "schedule-sources.json"),
    });
}
