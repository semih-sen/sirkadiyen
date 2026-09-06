using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Sirkadiyen.Worker.Configuration;

internal static class ConfigurationValueParser
{
    public static string Required(IConfiguration configuration, string key) =>
        configuration[key] is { } value && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException(
                $"Required configuration '{key}' is missing. Set it in the repository's '.env' "
                + $"file as '{key.Replace(":", "__", StringComparison.Ordinal)}' or export it as "
                + "an environment variable.");

    public static TimeOnly Time(string? value, TimeOnly fallback) =>
        string.IsNullOrWhiteSpace(value)
            ? fallback
            : TimeOnly.Parse(value, CultureInfo.InvariantCulture);

    public static double Double(string? value, double fallback) =>
        string.IsNullOrWhiteSpace(value)
            ? fallback
            : double.Parse(value, CultureInfo.InvariantCulture);

    public static int Integer(string? value, int fallback) =>
        string.IsNullOrWhiteSpace(value)
            ? fallback
            : int.Parse(value, CultureInfo.InvariantCulture);

    public static bool Bool(string? value, bool fallback) =>
        string.IsNullOrWhiteSpace(value)
            ? fallback
            : bool.Parse(value);

    public static TimeSpan Duration(string? value, TimeSpan fallback) =>
        string.IsNullOrWhiteSpace(value)
            ? fallback
            : TimeSpan.Parse(value, CultureInfo.InvariantCulture);
}
