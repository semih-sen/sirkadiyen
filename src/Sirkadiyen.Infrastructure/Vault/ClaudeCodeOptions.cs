namespace Sirkadiyen.Infrastructure.Vault;

/// <summary>How the Claude Code CLI is started for vault note jobs.</summary>
public sealed record ClaudeCodeOptions
{
    /// <summary>The <c>claude</c> executable, as an absolute path or a name on the service's PATH.</summary>
    public required string ExecutablePath { get; init; }

    /// <summary>
    /// How long one run may take before its process tree is killed. Writing a detailed note is well
    /// over the spec's 90 seconds in practice, so the default leaves room for it.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>A model alias or name, or null for the subscription's default.</summary>
    public string? Model { get; init; }

    /// <summary>The CLI's effort level (<c>low</c>, <c>medium</c>, <c>high</c>, ...), or null for its default.</summary>
    public string? Effort { get; init; }

    /// <summary>
    /// Where the CLI keeps its configuration, passed as <c>CLAUDE_CONFIG_DIR</c>. Needed on the server,
    /// where the service's home directory is not writable; null leaves the CLI's own default.
    /// </summary>
    public string? ConfigDirectory { get; init; }

    /// <summary>
    /// The only built-in tools the agent gets: enough to read the workspace and write the note, and no
    /// way to run commands or reach the network.
    /// </summary>
    public IReadOnlyList<string> Tools { get; init; } = ["Read", "Write", "Edit", "Glob"];

    /// <summary>
    /// The agent's standing rules, appended to its system prompt on every run, or null for none.
    /// </summary>
    /// <remarks>
    /// Passed on the command line rather than written into the workspace as <c>CLAUDE.md</c>:
    /// <c>--restricted</c> turns off <c>CLAUDE.md</c> discovery, which the end-to-end run showed as
    /// notes written without the rules, while an appended system prompt is honoured under it.
    /// </remarks>
    public string? StandingInstructions { get; init; }

    /// <summary>Well inside the 32K-character Windows command line, with room for the other arguments.</summary>
    public const int MaxStandingInstructionsLength = 16_000;

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ExecutablePath);
        if (Timeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("The Claude Code timeout must be positive.");
        }

        if (Tools.Count == 0 || Tools.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException("The Claude Code tool list must name at least one tool.");
        }

        if (StandingInstructions?.Length > MaxStandingInstructionsLength)
        {
            throw new InvalidOperationException(
                $"The Claude Code standing instructions must not exceed {MaxStandingInstructionsLength} characters.");
        }
    }
}
