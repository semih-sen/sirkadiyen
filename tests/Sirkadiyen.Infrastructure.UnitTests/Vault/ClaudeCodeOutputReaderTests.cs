using Sirkadiyen.Application.Vault;
using Sirkadiyen.Infrastructure.Vault;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests.Vault;

public sealed class ClaudeCodeOutputReaderTests
{
    [Fact]
    public void Reads_the_text_result()
    {
        VaultAgentResult result = ClaudeCodeOutputReader.Read(
            0,
            """{"type":"result","subtype":"success","is_error":false,"result":"tamam","num_turns":3}""",
            string.Empty);

        Assert.True(result.Succeeded);
        Assert.Equal("tamam", result.Output);
    }

    [Fact]
    public void Prefers_the_structured_output()
    {
        VaultAgentResult result = ClaudeCodeOutputReader.Read(
            0,
            """{"type":"result","is_error":false,"result":"Notu yazdım.","structured_output":{"folder":"","title":"Beta","backlinks":[]}}""",
            string.Empty);

        Assert.True(result.Succeeded);
        Assert.Equal("""{"folder":"","title":"Beta","backlinks":[]}""", result.Output);
    }

    /// <summary>The envelope the 2.1.280 CLI actually printed when run without a login.</summary>
    [Fact]
    public void Reports_the_reason_of_a_failed_run()
    {
        VaultAgentResult result = ClaudeCodeOutputReader.Read(
            1,
            """{"type":"result","subtype":"success","is_error":true,"terminal_reason":"api_error","result":"Not logged in · Please run /login","num_turns":1}""",
            string.Empty);

        Assert.False(result.Succeeded);
        Assert.Equal("Claude Code hata verdi (api_error): Not logged in · Please run /login", result.Failure);
    }

    [Fact]
    public void Treats_a_nonzero_exit_as_failure_even_without_the_flag()
    {
        VaultAgentResult result = ClaudeCodeOutputReader.Read(2, """{"type":"result","result":"yarım"}""", "stderr");

        Assert.False(result.Succeeded);
        Assert.Contains("yarım", result.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public void Finds_the_envelope_after_a_warning_line()
    {
        VaultAgentResult result = ClaudeCodeOutputReader.Read(
            0,
            "Uyarı: yapılandırma okunamadı\n{\"type\":\"result\",\"is_error\":false,\"result\":\"tamam\"}\n",
            string.Empty);

        Assert.Equal("tamam", result.Output);
    }

    [Theory]
    [InlineData("", "error: unknown option '--restricted'")]
    [InlineData("{\"not\":\"an envelope\"}", "")]
    public void Reports_unexpected_output_with_what_was_printed(string output, string error)
    {
        VaultAgentResult result = ClaudeCodeOutputReader.Read(1, output, error);

        Assert.False(result.Succeeded);
        Assert.StartsWith("Claude Code beklenmeyen çıktı verdi (çıkış kodu 1): ", result.Failure, StringComparison.Ordinal);
        Assert.EndsWith(error.Length > 0 ? error : output, result.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public void Caps_a_long_diagnostic()
    {
        VaultAgentResult result = ClaudeCodeOutputReader.Read(1, string.Empty, new string('x', 5000));

        Assert.True(result.Failure!.Length < 600);
    }
}
