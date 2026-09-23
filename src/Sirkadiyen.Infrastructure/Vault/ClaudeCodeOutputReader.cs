using System.Text.Json;
using Sirkadiyen.Application.Vault;

namespace Sirkadiyen.Infrastructure.Vault;

/// <summary>
/// Turns what <c>claude -p --output-format json</c> printed into a run result. The CLI prints one
/// result envelope even when it fails - not logged in, usage limit, API error - with the reason in
/// its <c>result</c> text, so that text is what a failed job reports.
/// </summary>
internal static class ClaudeCodeOutputReader
{
    /// <summary>Enough of an unexpected output to diagnose it from the job status; not a log dump.</summary>
    private const int MaxSnippetLength = 500;

    public static VaultAgentResult Read(int exitCode, string standardOutput, string standardError)
    {
        using JsonDocument? envelope = FindEnvelope(standardOutput);
        if (envelope is null)
        {
            string shown = Snippet(standardError) ?? Snippet(standardOutput) ?? "çıktı yok";
            return VaultAgentResult.Failed($"Claude Code beklenmeyen çıktı verdi (çıkış kodu {exitCode}): {shown}");
        }

        JsonElement root = envelope.RootElement;
        string? result = root.TryGetProperty("result", out JsonElement text) && text.ValueKind == JsonValueKind.String
            ? text.GetString()
            : null;
        bool isError = root.TryGetProperty("is_error", out JsonElement flag) && flag.ValueKind == JsonValueKind.True;

        if (isError || exitCode != 0)
        {
            string reason = StringProperty(root, "terminal_reason") ?? StringProperty(root, "subtype") ?? $"çıkış kodu {exitCode}";
            string detail = Snippet(result) ?? Snippet(standardError) ?? "ayrıntı yok";
            return VaultAgentResult.Failed($"Claude Code hata verdi ({reason}): {detail}");
        }

        // With --json-schema the validated answer arrives as its own field; the text result is then
        // only the model's prose. Without a schema the text result is the answer.
        if (root.TryGetProperty("structured_output", out JsonElement structured)
            && structured.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
        {
            return VaultAgentResult.Success(structured.GetRawText());
        }

        return VaultAgentResult.Success(result);
    }

    /// <summary>
    /// The whole output when it is one document; otherwise the last line that is a result envelope, in
    /// case a warning was printed to stdout ahead of it.
    /// </summary>
    private static JsonDocument? FindEnvelope(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        JsonDocument? whole = TryParseEnvelope(output);
        if (whole is not null)
        {
            return whole;
        }

        foreach (string line in output.ReplaceLineEndings("\n").Split('\n').Reverse())
        {
            if (line.TrimStart().StartsWith('{') && TryParseEnvelope(line) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static JsonDocument? TryParseEnvelope(string text)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }

        if (document.RootElement.ValueKind == JsonValueKind.Object
            && StringProperty(document.RootElement, "type") == "result")
        {
            return document;
        }

        document.Dispose();
        return null;
    }

    private static string? StringProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? Snippet(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string flattened = string.Join(' ', text.Split((char[])['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return flattened.Length <= MaxSnippetLength ? flattened : flattened[..MaxSnippetLength] + "…";
    }
}
