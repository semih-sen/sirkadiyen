using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace Sirkadiyen.Infrastructure.Security;

/// <summary>
/// Configures a Data Protection key ring that the API and the worker share (ADR-058).
/// </summary>
/// <remarks>
/// The worker decrypts a refresh token the API encrypted, so both hosts must protect with the
/// same keys. Data Protection isolates key rings by application name (defaulted to the content
/// root path) and, for a non-web host, is not configured at all. Pinning one application name
/// and one key location makes the two hosts share a ring so
/// <see cref="DataProtectionCalendarTokenProtector"/> can round-trip across processes (ADR-052,
/// ADR-057).
/// <para>
/// The file-system ring is correct for a single host. A multi-instance deployment must point
/// every instance at genuinely shared, backed-up storage instead.
/// </para>
/// </remarks>
public static class DataProtectionServiceCollectionExtensions
{
    /// <summary>The shared application name that binds both hosts to one key ring.</summary>
    public const string ApplicationName = "Sirkadiyen";

    public static IServiceCollection AddSirkadiyenDataProtection(
        this IServiceCollection services,
        string? keyRingPath)
    {
        ArgumentNullException.ThrowIfNull(services);

        string path = string.IsNullOrWhiteSpace(keyRingPath)
            ? DefaultKeyRingPath()
            : keyRingPath;
        Directory.CreateDirectory(path);

        services.AddDataProtection()
            .SetApplicationName(ApplicationName)
            .PersistKeysToFileSystem(new DirectoryInfo(path));

        return services;
    }

    private static string DefaultKeyRingPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ApplicationName,
        "DataProtection-Keys");
}
