using System.Globalization;
using System.Text.Json;
using Sirkadiyen.Contracts.Parsing;
using Sirkadiyen.Contracts.Serialization;

namespace Sirkadiyen.Application.Scheduling.Publication;

/// <summary>
/// Reads the out-of-sequence dates a parse reported, out of the response it
/// stored (ADR-139).
/// </summary>
/// <remarks>
/// The parser states these as warnings carrying a structured detail, because a
/// sentence is what an operator reads and a structured detail is what they act
/// on: accepting one of the listed candidates writes a source date correction.
/// <para>
/// A stored response is data the parser produced, not a contract this side
/// controls the shape of over time, so every field is read defensively. A
/// response this reader cannot make sense of yields no anomalies rather than
/// throwing: revision validation is the safety boundary in front of student
/// calendars, and it must not fail closed on the shape of a diagnostic.
/// </para>
/// </remarks>
public static class ParserDateAnomalyReader
{
    /// <summary>The parser warning codes this reader recognizes.</summary>
    public const string RepairedCode = "outOfSequenceDateRepaired";

    public const string SuggestedCode = "outOfSequenceDateNotRepaired";

    private static readonly JsonSerializerOptions JsonOptions = ContractJson.CreateOptions();

    /// <summary>
    /// The anomalies a stored parse response reports, in the order the parser
    /// recorded them. Empty when it stated none or could not be read.
    /// </summary>
    public static IReadOnlyList<ParserDateAnomaly> Read(string? parseResponse)
    {
        if (string.IsNullOrWhiteSpace(parseResponse))
        {
            return [];
        }

        ParseSnapshotResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<ParseSnapshotResponse>(parseResponse, JsonOptions);
        }
        catch (JsonException)
        {
            return [];
        }

        if (response is null)
        {
            return [];
        }

        List<ParserDateAnomaly> anomalies = [];
        foreach (ParserWarning warning in response.Warnings)
        {
            if (warning.Code is not (RepairedCode or SuggestedCode))
            {
                continue;
            }

            if (warning.Detail is { } detail && ReadAnomaly(detail, warning) is { } anomaly)
            {
                anomalies.Add(anomaly);
            }
        }

        return anomalies;
    }

    private static ParserDateAnomaly? ReadAnomaly(JsonElement detail, ParserWarning warning)
    {
        if (detail.ValueKind is not JsonValueKind.Object)
        {
            return null;
        }

        if (ReadDate(detail, "original") is not { } original)
        {
            // Without the date the source states there is nothing to correct
            // from, so the warning carries no decision an operator could make.
            return null;
        }

        return new ParserDateAnomaly
        {
            Original = original,
            Applied = ReadDate(detail, "applied"),
            LowerAnchor = ReadDate(detail, "lowerAnchor"),
            UpperAnchor = ReadDate(detail, "upperAnchor"),
            Reason = ReadString(detail, "reason") ?? warning.Code,
            Cell = warning.Evidence?.Range,
            Candidates = ReadCandidates(detail),
        };
    }

    private static IReadOnlyList<ParserDateCandidate> ReadCandidates(JsonElement detail)
    {
        if (!detail.TryGetProperty("candidates", out JsonElement candidates)
            || candidates.ValueKind is not JsonValueKind.Array)
        {
            return [];
        }

        List<ParserDateCandidate> read = [];
        foreach (JsonElement candidate in candidates.EnumerateArray())
        {
            if (candidate.ValueKind is not JsonValueKind.Object)
            {
                continue;
            }

            if (ReadDate(candidate, "value") is not { } value)
            {
                continue;
            }

            read.Add(new ParserDateCandidate
            {
                Value = value,
                Rule = ReadString(candidate, "rule") ?? string.Empty,
                WeekdayMatches = ReadBoolean(candidate, "weekdayMatches"),
            });
        }

        return read;
    }

    private static DateOnly? ReadDate(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value)
            && value.ValueKind is JsonValueKind.String
            && DateOnly.TryParseExact(
                value.GetString(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateOnly parsed)
            ? parsed
            : null;

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value)
            && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? ReadBoolean(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value)
            ? value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            }
            : null;
}
