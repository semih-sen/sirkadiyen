namespace Sirkadiyen.Application.Vault;

/// <summary>
/// Runs the coding agent once in a workspace directory. The agent may read and write files there and
/// nowhere else; what it reports back is its final text answer.
/// </summary>
public interface IVaultAgentRunner
{
    /// <param name="outputSchema">
    /// A JSON Schema the final answer must satisfy, or null for free text. When given, the answer is
    /// returned as the JSON document itself.
    /// </param>
    Task<VaultAgentResult> RunAsync(
        string workingDirectory,
        string prompt,
        string? outputSchema,
        CancellationToken cancellationToken);
}

public sealed record VaultAgentResult
{
    public required bool Succeeded { get; init; }

    /// <summary>The agent's final answer text, when it produced one.</summary>
    public string? Output { get; init; }

    /// <summary>Why the run failed: timeout, non-zero exit, usage limit. Null when it succeeded.</summary>
    public string? Failure { get; init; }

    public static VaultAgentResult Success(string? output) => new() { Succeeded = true, Output = output };

    public static VaultAgentResult Failed(string failure) => new() { Succeeded = false, Failure = failure };
}
