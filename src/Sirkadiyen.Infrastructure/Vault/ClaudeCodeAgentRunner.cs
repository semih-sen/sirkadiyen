using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Sirkadiyen.Application.Vault;

namespace Sirkadiyen.Infrastructure.Vault;

/// <summary>
/// Runs the Claude Code CLI headless in a job's workspace. The prompt goes in on stdin - a vault
/// catalog quickly outgrows a command line - and no shell is involved, so nothing in the prompt or
/// the schema is ever interpreted as a command.
/// </summary>
/// <remarks>
/// <c>--restricted</c> is what keeps the agent inside the workspace: it removes every tool that runs
/// commands or reaches the network, confines the file tools to the working directory, ignores
/// user and project settings files, and refuses permission bypass. It also skips <c>CLAUDE.md</c>
/// discovery, so the standing rules travel as an appended system prompt instead. <c>--bare</c> would
/// be stricter still but reads no OAuth credential, so it cannot run on a Claude subscription.
/// </remarks>
public sealed class ClaudeCodeAgentRunner(ClaudeCodeOptions options) : IVaultAgentRunner
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    public async Task<VaultAgentResult> RunAsync(
        string workingDirectory,
        string prompt,
        string? outputSchema,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        using Process process = new() { StartInfo = CreateStartInfo(workingDirectory, outputSchema) };
        try
        {
            process.Start();
        }
        catch (Win32Exception exception)
        {
            return VaultAgentResult.Failed($"Claude Code başlatılamadı ({options.ExecutablePath}): {exception.Message}");
        }

        // Both pipes are drained from the start: a process blocked writing to a full stderr pipe
        // would otherwise never exit and every run would end in a timeout.
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> standardError = process.StandardError.ReadToEndAsync(CancellationToken.None);

        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.Timeout);
        try
        {
            await WritePromptAsync(process, prompt, deadline.Token);
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException)
        {
            KillTree(process);
            cancellationToken.ThrowIfCancellationRequested();
            return VaultAgentResult.Failed(string.Create(
                CultureInfo.InvariantCulture,
                $"Claude Code {options.Timeout.TotalSeconds:0} saniye içinde bitmedi; süreç sonlandırıldı."));
        }

        return ClaudeCodeOutputReader.Read(process.ExitCode, await standardOutput, await standardError);
    }

    /// <summary>The CLI arguments for one run. Internal so the flag set can be pinned by tests.</summary>
    internal static IReadOnlyList<string> BuildArguments(ClaudeCodeOptions options, string? outputSchema)
    {
        List<string> arguments =
        [
            "-p",
            "--output-format", "json",
            "--restricted",
            "--tools", string.Join(',', options.Tools),

            // No MCP server from any configuration file joins the run.
            "--strict-mcp-config",
            "--no-session-persistence",

            // Edits inside the workspace are the job; anything that would still ask is denied, since
            // nobody is there to answer.
            "--permission-mode", "acceptEdits",
            "--permission-prompts", "none",
        ];

        if (!string.IsNullOrWhiteSpace(options.Model))
        {
            arguments.AddRange(["--model", options.Model]);
        }

        if (!string.IsNullOrWhiteSpace(options.Effort))
        {
            arguments.AddRange(["--effort", options.Effort]);
        }

        if (!string.IsNullOrWhiteSpace(options.StandingInstructions))
        {
            arguments.AddRange(["--append-system-prompt", options.StandingInstructions]);
        }

        if (!string.IsNullOrWhiteSpace(outputSchema))
        {
            arguments.AddRange(["--json-schema", outputSchema]);
        }

        return arguments;
    }

    private ProcessStartInfo CreateStartInfo(string workingDirectory, string? outputSchema)
    {
        ProcessStartInfo start = new()
        {
            FileName = options.ExecutablePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Utf8WithoutBom,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (string argument in BuildArguments(options, outputSchema))
        {
            start.ArgumentList.Add(argument);
        }

        // A service updates the CLI by deployment, not by the CLI rewriting itself mid-run, and it
        // has no use for the CLI's telemetry or error reporting.
        start.Environment["DISABLE_AUTOUPDATER"] = "1";
        start.Environment["CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC"] = "1";
        if (!string.IsNullOrWhiteSpace(options.ConfigDirectory))
        {
            start.Environment["CLAUDE_CONFIG_DIR"] = options.ConfigDirectory;
        }

        return start;
    }

    private static async Task WritePromptAsync(Process process, string prompt, CancellationToken cancellationToken)
    {
        try
        {
            await process.StandardInput.WriteAsync(prompt.AsMemory(), cancellationToken);
            await process.StandardInput.FlushAsync(cancellationToken);
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // The process exited before reading its input - a startup failure. Its exit code and
            // output say why, which is more useful than the broken pipe.
        }
    }

    private static void KillTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // It exited between the deadline and the kill.
        }
    }
}
