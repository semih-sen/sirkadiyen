namespace Sirkadiyen.Worker;

internal sealed record WorkerOptions
{
    public required string SourceCatalogPath { get; init; }
}
