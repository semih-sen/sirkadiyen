using System.Diagnostics;
using System.Text;
using Sirkadiyen.Application.Vault;
using Sirkadiyen.Infrastructure.Vault;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests.Vault;

/// <summary>
/// Runs the runner against a stand-in <c>claude</c>: a batch file on Windows, a shell script
/// elsewhere. What is pinned down is the process plumbing - stdin, working directory, environment,
/// timeout and kill - which a fake runner could not show.
/// </summary>
public sealed class ClaudeCodeAgentRunnerTests : IDisposable
{
    private const string Envelope = """{"type":"result","subtype":"success","is_error":false,"result":"tamam"}""";

    private readonly string root = Path.Combine(Path.GetTempPath(), "sirkadiyen-claude-tests", Guid.NewGuid().ToString("N"));
    private readonly string workspace;
    private readonly string tools;

    public ClaudeCodeAgentRunnerTests()
    {
        workspace = Directory.CreateDirectory(Path.Combine(root, "workspace")).FullName;
        tools = Directory.CreateDirectory(Path.Combine(root, "bin")).FullName;
        File.WriteAllText(Path.Combine(tools, "response.json"), Envelope);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Passes_the_prompt_on_stdin_in_the_workspace_and_reads_the_result()
    {
        string executable = FakeClaude(
            windows: """
                findstr "^" > prompt.txt
                > config-dir.txt echo %CLAUDE_CONFIG_DIR%
                > autoupdater.txt echo %DISABLE_AUTOUPDATER%
                type "%~dp0response.json"
                """,
            unix: """
                cat > prompt.txt
                printf '%s' "$CLAUDE_CONFIG_DIR" > config-dir.txt
                printf '%s' "$DISABLE_AUTOUPDATER" > autoupdater.txt
                cat "$(dirname "$0")/response.json"
                """);
        ClaudeCodeAgentRunner runner = new(Options(executable) with { ConfigDirectory = Path.Combine(root, "config") });
        const string Prompt = "Beta blokerler hakkında özet — ğüşiöçİ\nikinci satır";

        VaultAgentResult result = await runner.RunAsync(workspace, Prompt, outputSchema: null, CancellationToken.None);

        Assert.True(result.Succeeded, result.Failure);
        Assert.Equal("tamam", result.Output);
        Assert.Equal(Prompt, File.ReadAllText(Path.Combine(workspace, "prompt.txt"), Encoding.UTF8).ReplaceLineEndings("\n").TrimEnd('\n'));
        Assert.False(HasByteOrderMark(Path.Combine(workspace, "prompt.txt")));
        Assert.Equal(Path.Combine(root, "config"), File.ReadAllText(Path.Combine(workspace, "config-dir.txt")).Trim());
        Assert.Equal("1", File.ReadAllText(Path.Combine(workspace, "autoupdater.txt")).Trim());
    }

    [Fact]
    public async Task Reports_a_failing_run()
    {
        File.WriteAllText(
            Path.Combine(tools, "response.json"),
            """{"type":"result","is_error":true,"terminal_reason":"api_error","result":"Not logged in"}""");
        string executable = FakeClaude(
            windows: """
                type "%~dp0response.json"
                exit /b 1
                """,
            unix: """
                cat "$(dirname "$0")/response.json"
                exit 1
                """);

        VaultAgentResult result = await new ClaudeCodeAgentRunner(Options(executable))
            .RunAsync(workspace, "x", outputSchema: null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Claude Code hata verdi (api_error): Not logged in", result.Failure);
    }

    [Fact]
    public async Task Kills_a_run_that_outlives_the_timeout()
    {
        string executable = FakeClaude(
            windows: "ping -n 60 127.0.0.1 > nul",
            unix: "sleep 60");
        ClaudeCodeAgentRunner runner = new(Options(executable) with { Timeout = TimeSpan.FromSeconds(1) });

        Stopwatch elapsed = Stopwatch.StartNew();
        VaultAgentResult result = await runner.RunAsync(workspace, "x", outputSchema: null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("süreç sonlandırıldı", result.Failure, StringComparison.Ordinal);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(30), $"took {elapsed.Elapsed}");
    }

    [Fact]
    public async Task Propagates_the_caller_cancelling()
    {
        string executable = FakeClaude(windows: "ping -n 60 127.0.0.1 > nul", unix: "sleep 60");
        using CancellationTokenSource cancel = new(TimeSpan.FromSeconds(1));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ClaudeCodeAgentRunner(Options(executable)).RunAsync(workspace, "x", outputSchema: null, cancel.Token));
    }

    [Fact]
    public async Task Reports_a_missing_executable()
    {
        VaultAgentResult result = await new ClaudeCodeAgentRunner(Options(Path.Combine(tools, "yok-claude")))
            .RunAsync(workspace, "x", outputSchema: null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.StartsWith("Claude Code başlatılamadı", result.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public void Arguments_confine_the_agent_and_carry_the_schema()
    {
        IReadOnlyList<string> arguments = ClaudeCodeAgentRunner.BuildArguments(
            Options("claude") with { Model = "claude-sonnet-5", Effort = "medium", StandingInstructions = "# Kurallar" },
            """{"type":"object"}""");

        Assert.Equal(
            [
                "-p", "--output-format", "json", "--restricted", "--tools", "Read,Write,Edit,Glob",
                "--strict-mcp-config", "--no-session-persistence",
                "--permission-mode", "acceptEdits", "--permission-prompts", "none",
                "--model", "claude-sonnet-5", "--effort", "medium",
                "--append-system-prompt", "# Kurallar",
                "--json-schema", """{"type":"object"}""",
            ],
            arguments);
        Assert.DoesNotContain("--dangerously-skip-permissions", arguments);
    }

    [Fact]
    public void Arguments_omit_what_is_not_configured()
    {
        IReadOnlyList<string> arguments = ClaudeCodeAgentRunner.BuildArguments(Options("claude"), outputSchema: null);

        Assert.DoesNotContain("--model", arguments);
        Assert.DoesNotContain("--effort", arguments);
        Assert.DoesNotContain("--append-system-prompt", arguments);
        Assert.DoesNotContain("--json-schema", arguments);
    }

    private static ClaudeCodeOptions Options(string executable) => new() { ExecutablePath = executable, Timeout = TimeSpan.FromSeconds(30) };

    private string FakeClaude(string windows, string unix)
    {
        if (OperatingSystem.IsWindows())
        {
            string batch = Path.Combine(tools, "claude.cmd");
            File.WriteAllText(batch, "@echo off\r\n" + windows.ReplaceLineEndings("\r\n") + "\r\n");
            return batch;
        }

        string script = Path.Combine(tools, "claude");
        File.WriteAllText(script, "#!/bin/sh\n" + unix.ReplaceLineEndings("\n") + "\n");
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return script;
    }

    private static bool HasByteOrderMark(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        return bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
    }
}
