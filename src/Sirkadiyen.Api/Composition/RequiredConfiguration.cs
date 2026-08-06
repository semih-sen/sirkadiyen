namespace Sirkadiyen.Api.Composition;

/// <summary>
/// Fail-fast reader for configuration values the API cannot start without.
/// </summary>
internal static class RequiredConfiguration
{
    public static string Get(IConfiguration configuration, string key) =>
        configuration[key] is { } value && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException(
                $"Required configuration '{key}' is missing. Set it in the repository's '.env' "
                + $"file as '{key.Replace(":", "__", StringComparison.Ordinal)}' or export it as "
                + "an environment variable.");
}
