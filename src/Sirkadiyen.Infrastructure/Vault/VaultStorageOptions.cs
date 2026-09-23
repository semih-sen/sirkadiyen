namespace Sirkadiyen.Infrastructure.Vault;

/// <summary>Where the Obsidian vault is stored: an S3-compatible bucket, in practice MinIO.</summary>
public sealed record VaultStorageOptions
{
    /// <summary>The S3 endpoint, e.g. <c>http://127.0.0.1:9000</c>.</summary>
    public required Uri Endpoint { get; init; }

    public required string AccessKey { get; init; }

    public required string SecretKey { get; init; }

    public required string Bucket { get; init; }

    /// <summary>
    /// The key prefix the vault lives under when it shares the bucket with other data; empty when the
    /// vault is the whole bucket. Normalized to end in exactly one slash.
    /// </summary>
    public string Prefix { get; init; } = string.Empty;

    /// <summary>MinIO ignores the region, but request signing still needs one; this is MinIO's default.</summary>
    public string Region { get; init; } = "us-east-1";

    public string NormalizedPrefix =>
        Prefix.Trim().Trim('/') is { Length: > 0 } trimmed ? trimmed + "/" : string.Empty;

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Endpoint);
        if (!Endpoint.IsAbsoluteUri)
        {
            throw new InvalidOperationException("The vault storage endpoint must be an absolute URI.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(AccessKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(SecretKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(Bucket);
        ArgumentException.ThrowIfNullOrWhiteSpace(Region);
    }
}
