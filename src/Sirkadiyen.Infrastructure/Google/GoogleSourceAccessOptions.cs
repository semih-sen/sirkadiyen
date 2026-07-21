namespace Sirkadiyen.Infrastructure.Google;

public sealed record GoogleSourceAccessOptions
{
    public const string ConfigurationSection = "SIRKADIYEN_GOOGLE";

    public string? ClientId { get; init; }

    public string? ClientSecret { get; init; }

    public string? SourceRefreshToken { get; init; }

    public string? ServiceAccountCredentialPath { get; init; }
}
