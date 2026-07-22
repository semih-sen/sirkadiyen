using Sirkadiyen.Infrastructure.Configuration;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

public sealed class DotEnvFileTests
{
    [Fact]
    public void ReadsDeclarationsInOrderAndIgnoresBlanksAndComments()
    {
        IReadOnlyList<KeyValuePair<string, string>> variables = Parse(
            """
            # A comment.

              # An indented comment.
            SIRKADIYEN_PARSER__BASE_URL=http://localhost:8000
            export SIRKADIYEN_POLLING__TIME_ZONE_ID=Europe/Istanbul
            """);

        Assert.Equal(
            [
                new KeyValuePair<string, string>("SIRKADIYEN_PARSER__BASE_URL", "http://localhost:8000"),
                new KeyValuePair<string, string>("SIRKADIYEN_POLLING__TIME_ZONE_ID", "Europe/Istanbul"),
            ],
            variables);
    }

    [Fact]
    public void KeepsEveryEqualsSignAfterTheFirstOne()
    {
        // The reason this parser exists: a connection string is mostly separators.
        IReadOnlyList<KeyValuePair<string, string>> variables = Parse(
            "SIRKADIYEN_DATABASE__CONNECTION_STRING = Host=localhost;Port=15432;Database=sirkadiyen");

        KeyValuePair<string, string> variable = Assert.Single(variables);
        Assert.Equal("SIRKADIYEN_DATABASE__CONNECTION_STRING", variable.Key);
        Assert.Equal("Host=localhost;Port=15432;Database=sirkadiyen", variable.Value);
    }

    [Fact]
    public void TakesAnUnquotedValueLiterallyToTheEndOfTheLine()
    {
        // There is no inline comment syntax, because a password may contain '#'.
        IReadOnlyList<KeyValuePair<string, string>> variables = Parse("PASSWORD=p#ss word");

        Assert.Equal("p#ss word", Assert.Single(variables).Value);
    }

    [Theory]
    [InlineData("PASSWORD='  literal\\n  '", "  literal\\n  ")]
    [InlineData("PASSWORD=\"line\\nbreak\"", "line\nbreak")]
    [InlineData("PASSWORD=\"quote\\\" and \\\\ slash\"", "quote\" and \\ slash")]
    [InlineData("PASSWORD=\"\"", "")]
    public void ResolvesQuotingAccordingToTheQuoteCharacter(string line, string expected)
    {
        Assert.Equal(expected, Assert.Single(Parse(line)).Value);
    }

    [Theory]
    [InlineData("no separator at all")]
    [InlineData("=orphan")]
    [InlineData("HAS SPACE=value")]
    [InlineData("1LEADING_DIGIT=value")]
    [InlineData("PASSWORD=\"unclosed")]
    [InlineData("PASSWORD=\"dangling\\\"")]
    [InlineData("PASSWORD=\"unsupported\\q\"")]
    public void RefusesAMalformedLineRatherThanDroppingIt(string line)
    {
        // A dropped setting resurfaces as a missing-configuration failure much
        // later, with nothing pointing at the typo that caused it.
        Assert.Throws<InvalidDataException>(() => Parse(line));
    }

    [Fact]
    public void NamesTheOffendingFileAndLineWithoutQuotingTheValue()
    {
        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => DotEnvFile.Parse(new StringReader("GOOD=1\n\nSECRET \"swallow-me\"\n"), "/etc/app/.env"));

        Assert.Contains("/etc/app/.env", exception.Message, StringComparison.Ordinal);
        Assert.Contains("line 3", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("swallow-me", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FindsTheNearestFileAboveTheStartingDirectory()
    {
        using TemporaryDirectory root = new();
        string expected = Path.Combine(root.Path, ".env");
        File.WriteAllText(expected, "KEY=value\n");
        string nested = Directory.CreateDirectory(
            Path.Combine(root.Path, "src", "Sirkadiyen.Api", "bin", "Debug", "net10.0")).FullName;

        Assert.Equal(expected, DotEnvFile.Find(nested));
    }

    [Fact]
    public void ReportsNoFileWhenNoneExists()
    {
        using TemporaryDirectory root = new();

        DotEnvLoadResult result = DotEnvFile.Load(root.Path, Guid.CreateVersion7().ToString("N"));

        Assert.Null(result.FilePath);
        Assert.Equal(0, result.AppliedCount);
    }

    [Fact]
    public void AppliesOnlyVariablesTheEnvironmentDoesNotAlreadyDefine()
    {
        // An injected or exported value stays authoritative, so a stale file
        // cannot quietly redirect a host at the wrong database.
        using TemporaryDirectory root = new();
        string fileName = Guid.CreateVersion7().ToString("N");
        string existing = $"SIRKADIYEN_TEST_{Guid.CreateVersion7():N}";
        string missing = $"SIRKADIYEN_TEST_{Guid.CreateVersion7():N}";
        File.WriteAllText(
            Path.Combine(root.Path, fileName),
            $"{existing}=from-file\n{missing}=from-file\n");
        Environment.SetEnvironmentVariable(existing, "from-environment");

        try
        {
            DotEnvLoadResult result = DotEnvFile.Load(root.Path, fileName);

            Assert.Equal(1, result.AppliedCount);
            Assert.Equal(1, result.SkippedCount);
            Assert.Equal("from-environment", Environment.GetEnvironmentVariable(existing));
            Assert.Equal("from-file", Environment.GetEnvironmentVariable(missing));
        }
        finally
        {
            Environment.SetEnvironmentVariable(existing, null);
            Environment.SetEnvironmentVariable(missing, null);
        }
    }

    private static IReadOnlyList<KeyValuePair<string, string>> Parse(string content) =>
        DotEnvFile.Parse(new StringReader(content));

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() =>
            Path = Directory.CreateTempSubdirectory("sirkadiyen-dotenv-").FullName;

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
