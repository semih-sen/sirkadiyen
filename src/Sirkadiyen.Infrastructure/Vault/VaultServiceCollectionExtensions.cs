using Amazon.S3;
using Microsoft.Extensions.DependencyInjection;
using Sirkadiyen.Application.Vault;

namespace Sirkadiyen.Infrastructure.Vault;

public static class VaultServiceCollectionExtensions
{
    /// <summary>Registers the S3 client and the vault store over it.</summary>
    public static IServiceCollection AddSirkadiyenVaultStorage(
        this IServiceCollection services,
        VaultStorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        services.AddSingleton(options);

        // Keyed, so this client can never be resolved by something expecting an AWS S3 client.
        services.AddKeyedSingleton<IAmazonS3>(nameof(S3VaultStore), (_, _) => S3VaultStore.CreateClient(options));
        services.AddSingleton<IVaultStore>(provider => new S3VaultStore(
            provider.GetRequiredKeyedService<IAmazonS3>(nameof(S3VaultStore)),
            options));
        return services;
    }

    /// <summary>Registers the Claude Code CLI as the agent that writes vault notes.</summary>
    public static IServiceCollection AddSirkadiyenClaudeCodeAgent(
        this IServiceCollection services,
        ClaudeCodeOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        services.AddSingleton(options);
        services.AddSingleton<IVaultAgentRunner, ClaudeCodeAgentRunner>();
        return services;
    }
}
