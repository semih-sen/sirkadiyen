using System.Text;

namespace Sirkadiyen.Infrastructure.Configuration;

/// <summary>
/// Loads the repository's <c>.env</c> file into the process environment so that
/// <c>dotnet run</c> works from a project directory without exporting every
/// variable by hand.
/// </summary>
/// <remarks>
/// <para>
/// This is a development convenience, not a configuration source. Deployed
/// environments inject real environment variables and ship no <c>.env</c> file,
/// so the loader is a no-op there. A variable that is already set is never
/// overwritten, which keeps an injected or exported value authoritative and
/// stops a stale file from quietly redirecting a host at the wrong database.
/// </para>
/// <para>
/// Values are secrets. Nothing here logs, returns, or throws a value; failures
/// report the file path and the line number only.
/// </para>
/// </remarks>
public static class DotEnvFile
{
    /// <summary>
    /// The conventional file name, matching the repository's <c>.gitignore</c>.
    /// </summary>
    public const string DefaultFileName = ".env";

    /// <summary>
    /// Finds the nearest environment file at or above <paramref name="startDirectory"/>
    /// and applies every variable it declares that is not already set.
    /// </summary>
    /// <param name="startDirectory">
    /// Where to start searching. Defaults to the directory the assembly was
    /// loaded from, which under <c>dotnet run</c> sits several levels below the
    /// repository root.
    /// </param>
    /// <param name="fileName">The file name to search for.</param>
    /// <returns>The file that was used, if any, and what it did.</returns>
    /// <exception cref="InvalidDataException">The file exists but is malformed.</exception>
    public static DotEnvLoadResult Load(string? startDirectory = null, string fileName = DefaultFileName)
    {
        string? path = Find(startDirectory ?? AppContext.BaseDirectory, fileName);
        if (path is null)
        {
            return new DotEnvLoadResult(null, 0, 0);
        }

        using StreamReader reader = new(path, Encoding.UTF8);
        IReadOnlyList<KeyValuePair<string, string>> variables = Parse(reader, path);

        int applied = 0;
        int skipped = 0;
        foreach ((string key, string value) in variables)
        {
            if (Environment.GetEnvironmentVariable(key) is not null)
            {
                skipped++;
                continue;
            }

            Environment.SetEnvironmentVariable(key, value);
            applied++;
        }

        return new DotEnvLoadResult(path, applied, skipped);
    }

    /// <summary>
    /// Walks upwards from <paramref name="startDirectory"/> looking for
    /// <paramref name="fileName"/>, returning the first match.
    /// </summary>
    public static string? Find(string startDirectory, string fileName = DefaultFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(startDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        DirectoryInfo? directory = new(startDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }

    /// <summary>
    /// Parses environment-file text into declaration order.
    /// </summary>
    /// <remarks>
    /// Blank lines and lines whose first non-whitespace character is <c>#</c> are
    /// ignored. A declaration is <c>KEY=VALUE</c>, optionally prefixed with
    /// <c>export</c>. Only the first <c>=</c> separates the two, so a connection
    /// string keeps its own. An unquoted value is trimmed and taken literally to
    /// the end of the line: there is no inline comment syntax, because a
    /// password may legitimately contain <c>#</c>. A value wrapped in single
    /// quotes is taken verbatim; one wrapped in double quotes resolves the
    /// escapes <c>\\</c>, <c>\"</c>, <c>\n</c>, <c>\r</c> and <c>\t</c>.
    /// Anything else is an error rather than a silently dropped setting.
    /// </remarks>
    /// <param name="reader">The text to parse.</param>
    /// <param name="path">The file path, used only to describe a failure.</param>
    /// <exception cref="InvalidDataException">A line is neither blank, a comment, nor a declaration.</exception>
    public static IReadOnlyList<KeyValuePair<string, string>> Parse(TextReader reader, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(reader);

        List<KeyValuePair<string, string>> variables = [];
        int lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            string trimmed = line.Trim();
            if (trimmed.Length is 0 || trimmed[0] is '#')
            {
                continue;
            }

            if (trimmed.StartsWith("export ", StringComparison.Ordinal))
            {
                trimmed = trimmed["export ".Length..].TrimStart();
            }

            int separator = trimmed.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                throw Malformed(path, lineNumber, "expected KEY=VALUE");
            }

            string key = trimmed[..separator].TrimEnd();
            if (!IsValidKey(key))
            {
                throw Malformed(path, lineNumber, "the name is not a valid environment variable name");
            }

            variables.Add(new KeyValuePair<string, string>(
                key,
                ReadValue(trimmed[(separator + 1)..].Trim(), path, lineNumber)));
        }

        return variables;
    }

    private static string ReadValue(string raw, string? path, int lineNumber)
    {
        if (raw.Length < 2 || raw[0] is not ('"' or '\''))
        {
            return raw;
        }

        char quote = raw[0];
        if (raw[^1] != quote)
        {
            throw Malformed(path, lineNumber, "the quoted value is not closed");
        }

        string quoted = raw[1..^1];
        return quote is '\'' ? quoted : Unescape(quoted, path, lineNumber);
    }

    private static string Unescape(string value, string? path, int lineNumber)
    {
        if (!value.Contains('\\', StringComparison.Ordinal))
        {
            return value;
        }

        StringBuilder builder = new(value.Length);
        for (int index = 0; index < value.Length; index++)
        {
            if (value[index] is not '\\')
            {
                builder.Append(value[index]);
                continue;
            }

            if (index + 1 == value.Length)
            {
                throw Malformed(path, lineNumber, "the value ends with a dangling escape");
            }

            builder.Append(value[++index] switch
            {
                '\\' => '\\',
                '"' => '"',
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                _ => throw Malformed(path, lineNumber, "the value contains an unsupported escape"),
            });
        }

        return builder.ToString();
    }

    private static bool IsValidKey(string key)
    {
        if (key.Length is 0 || char.IsAsciiDigit(key[0]))
        {
            return false;
        }

        foreach (char character in key)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('_' or '.' or ':'))
            {
                return false;
            }
        }

        return true;
    }

    private static InvalidDataException Malformed(string? path, int lineNumber, string reason) =>
        new($"'{path ?? DefaultFileName}' line {lineNumber} is malformed: {reason}.");
}

/// <summary>
/// What <see cref="DotEnvFile.Load(string?, string)"/> did.
/// </summary>
/// <param name="FilePath">The file that was used, or <see langword="null"/> when none was found.</param>
/// <param name="AppliedCount">Variables written into the process environment.</param>
/// <param name="SkippedCount">Variables the environment already defined, which stay untouched.</param>
public sealed record DotEnvLoadResult(string? FilePath, int AppliedCount, int SkippedCount);
