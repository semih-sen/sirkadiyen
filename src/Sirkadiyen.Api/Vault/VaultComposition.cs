using System.Globalization;
using Sirkadiyen.Api.Composition;
using Sirkadiyen.Application.Vault;
using Sirkadiyen.Infrastructure.Vault;

namespace Sirkadiyen.Api.Vault;

/// <summary>What the vault endpoints need at request time: the key that admits a caller.</summary>
internal sealed record VaultApiOptions(string ApiKey);

/// <summary>
/// Registers the personal vault feature (ADR-168) from <c>SIRKADIYEN_VAULT:*</c>. The feature is off
/// unless <c>API_KEY</c> is set; once it is, every storage setting is required, so a half-configured
/// deployment fails at startup instead of on the first note.
/// </summary>
internal static class VaultComposition
{
    private const string Section = "SIRKADIYEN_VAULT:";

    /// <summary>Short keys are guessable; the endpoint is reachable from the internet.</summary>
    private const int MinimumApiKeyLength = 32;

    /// <summary>
    /// The agent's standing rules, shipped beside the API binaries and used unless a path is configured.
    /// They reach the agent as an appended system prompt on every run.
    /// </summary>
    private const string DefaultInstructionsFile = "Vault/AgentInstructions.md";

    public static void AddSirkadiyenVault(this IServiceCollection services, IConfiguration configuration, string contentRoot)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        string? apiKey = configuration[Section + "API_KEY"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return;
        }

        if (apiKey.Length < MinimumApiKeyLength)
        {
            throw new InvalidOperationException(
                $"'{Section}API_KEY' must be at least {MinimumApiKeyLength} characters.");
        }

        services.AddSingleton(new VaultApiOptions(apiKey));

        services.AddSirkadiyenVaultStorage(new VaultStorageOptions
        {
            Endpoint = new Uri(RequiredConfiguration.Get(configuration, Section + "S3_ENDPOINT"), UriKind.Absolute),
            AccessKey = RequiredConfiguration.Get(configuration, Section + "S3_ACCESS_KEY"),
            SecretKey = RequiredConfiguration.Get(configuration, Section + "S3_SECRET_KEY"),
            Bucket = RequiredConfiguration.Get(configuration, Section + "S3_BUCKET"),
            Prefix = configuration[Section + "S3_PREFIX"] ?? string.Empty,
            Region = Optional(configuration, "S3_REGION") ?? "us-east-1",
        });

        string instructionsPath = Optional(configuration, "AGENT_INSTRUCTIONS_PATH") is { } configured
            ? Path.GetFullPath(configured, contentRoot)
            : Path.Combine(AppContext.BaseDirectory, DefaultInstructionsFile);
        if (!File.Exists(instructionsPath))
        {
            throw new InvalidOperationException($"The vault agent instructions file '{instructionsPath}' does not exist.");
        }

        services.AddSirkadiyenClaudeCodeAgent(new ClaudeCodeOptions
        {
            ExecutablePath = Optional(configuration, "CLAUDE_PATH") ?? "claude",
            Model = Optional(configuration, "CLAUDE_MODEL") ?? "claude-sonnet-5",
            Effort = Optional(configuration, "CLAUDE_EFFORT") ?? "medium",
            ConfigDirectory = Optional(configuration, "CLAUDE_CONFIG_DIR"),
            Timeout = Optional(configuration, "AGENT_TIMEOUT") is { } timeout
                ? TimeSpan.Parse(timeout, CultureInfo.InvariantCulture)
                : TimeSpan.FromMinutes(5),
            StandingInstructions = File.ReadAllText(instructionsPath),
        });

        VaultNoteOptions noteOptions = new()
        {
            // The whole directory is the feature's own: the startup sweep empties it.
            WorkspaceRoot = Path.GetFullPath(
                Optional(configuration, "WORKSPACE_ROOT") ?? Path.Combine(Path.GetTempPath(), "sirkadiyen-vault-agent")),
            MaxBacklinks = Optional(configuration, "MAX_BACKLINKS") is { } maxBacklinks
                ? int.Parse(maxBacklinks, CultureInfo.InvariantCulture)
                : 5,
        };
        noteOptions.Validate();

        services.AddSingleton(noteOptions);
        services.AddSingleton<VaultJobRegistry>();
        services.AddSingleton<VaultNoteJobService>();
        services.AddSingleton<VaultApiKeyFilter>();
        services.AddHostedService<VaultJobProcessor>();
    }

    private static string? Optional(IConfiguration configuration, string key) =>
        configuration[Section + key] is { Length: > 0 } value && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
}
