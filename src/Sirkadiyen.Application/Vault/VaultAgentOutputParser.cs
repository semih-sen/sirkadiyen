using System.Text.Json;

namespace Sirkadiyen.Application.Vault;

/// <summary>
/// Reads the JSON answer the agent is asked to end the note-writing run with. The agent is a language
/// model, so the answer may arrive wrapped in a code fence or after a sentence of prose; this finds
/// the object rather than insisting on a bare document.
/// </summary>
public static class VaultAgentOutputParser
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static VaultNoteDraft? ParseNoteDraft(string? output, out string? error)
    {
        error = null;
        string? json = ExtractJsonObject(output);
        if (json is null)
        {
            error = "Agent yanıtında JSON bulunamadı.";
            return null;
        }

        DraftDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<DraftDocument>(json, SerializerOptions);
        }
        catch (JsonException exception)
        {
            error = $"Agent yanıtındaki JSON okunamadı: {exception.Message}";
            return null;
        }

        if (document is null)
        {
            error = "Agent yanıtındaki JSON boş.";
            return null;
        }

        List<VaultBacklinkRequest> backlinks = (document.Backlinks ?? [])
            .Where(static item => item is not null && !string.IsNullOrWhiteSpace(item.Target))
            .Select(static item => new VaultBacklinkRequest(item!.Target!.Trim(), item.Reason?.Trim()))
            .ToList();

        return new VaultNoteDraft(document.Folder?.Trim(), document.Title?.Trim(), backlinks);
    }

    /// <summary>
    /// The last fenced block when there is one, since that is where a model puts "the answer";
    /// otherwise the span from the first opening brace to the last closing one.
    /// </summary>
    private static string? ExtractJsonObject(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        const string Fence = "```";
        int fenceEnd = output.LastIndexOf(Fence, StringComparison.Ordinal);
        if (fenceEnd > 0)
        {
            int fenceStart = output.LastIndexOf(Fence, fenceEnd - 1, StringComparison.Ordinal);
            if (fenceStart >= 0)
            {
                string fenced = output[(fenceStart + Fence.Length)..fenceEnd];
                int newline = fenced.IndexOf('\n', StringComparison.Ordinal);
                string body = newline >= 0 && !fenced[..newline].Contains('{', StringComparison.Ordinal)
                    ? fenced[(newline + 1)..]
                    : fenced;
                if (body.TrimStart().StartsWith('{'))
                {
                    return body.Trim();
                }
            }
        }

        int open = output.IndexOf('{', StringComparison.Ordinal);
        int close = output.LastIndexOf('}');
        return open >= 0 && close > open ? output[open..(close + 1)] : null;
    }

    private sealed class DraftDocument
    {
        public string? Folder { get; set; }

        public string? Title { get; set; }

        public List<BacklinkDocument?>? Backlinks { get; set; }
    }

    private sealed class BacklinkDocument
    {
        public string? Target { get; set; }

        public string? Reason { get; set; }
    }
}

/// <summary>What the agent proposed after writing the note. Every field is a proposal, not yet validated.</summary>
public sealed record VaultNoteDraft(string? Folder, string? Title, IReadOnlyList<VaultBacklinkRequest> Backlinks);

public sealed record VaultBacklinkRequest(string Target, string? Reason);
