using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.DependencyInjection;
using Sirkadiyen.Application.Scheduling.Ingestion;

namespace Sirkadiyen.Infrastructure.Google;

public static class GoogleSourceServiceCollectionExtensions
{
    /// <summary>
    /// How long one Drive request may take. The real documents are tens of
    /// kilobytes, so this bounds a hung connection rather than a slow download,
    /// and stays well inside one polling interval.
    /// </summary>
    public static readonly TimeSpan DefaultDriveTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Registers the Google Drive source-document client and the credential it
    /// shares with the Sheets adapter (ADR-083).
    /// </summary>
    public static IServiceCollection AddSirkadiyenGoogleDriveClient(
        this IServiceCollection services,
        GoogleSourceAccessOptions options,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton<GoogleSourceCredentialFactory>();

        // A singleton because the credential holds the refreshed access token. One
        // per scope would mint a new token for every polling cycle and lose the
        // refresh the previous one had already done.
        services.AddSingleton<ICredential>(provider =>
            provider.GetRequiredService<GoogleSourceCredentialFactory>().Create(options));
        services.AddTransient<GoogleSourceAccessTokenHandler>();

        services
            .AddHttpClient<IGoogleDriveFileClient, GoogleDriveHttpClient>(client =>
            {
                client.BaseAddress = new Uri(GoogleDriveHttpClient.BaseAddress);
                client.Timeout = timeout ?? DefaultDriveTimeout;
            })
            .AddHttpMessageHandler<GoogleSourceAccessTokenHandler>();

        // Listing a folder is a separate port from reading a file, so it gets its
        // own client and its own timeout budget (ADR-133).
        services
            .AddHttpClient<IGoogleDriveFolderClient, GoogleDriveFolderHttpClient>(client =>
            {
                client.BaseAddress = new Uri(GoogleDriveFolderHttpClient.BaseAddress);
                client.Timeout = timeout ?? DefaultDriveTimeout;
            })
            .AddHttpMessageHandler<GoogleSourceAccessTokenHandler>();

        services.AddSingleton<IWeeklyDocumentDiscovery, WeeklyDocumentDiscovery>();

        return services;
    }
}
