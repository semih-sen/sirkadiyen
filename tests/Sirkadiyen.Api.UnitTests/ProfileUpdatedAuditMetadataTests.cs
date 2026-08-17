using System.Text.Json;
using Sirkadiyen.Api.StudentProfiles;
using Sirkadiyen.Contracts.Serialization;
using Xunit;

namespace Sirkadiyen.Api.UnitTests;

/// <summary>
/// The structured detail written with a <c>ProfileUpdated</c> audit event. A profile change can
/// retire calendar events the previous audience received (ADR-096), so this payload is the trail
/// that answers "why did these lessons disappear" — and it must not become a place a student's
/// identifying number leaks into an operator-readable log (AI_GUIDELINE §15, §19).
/// </summary>
public sealed class ProfileUpdatedAuditMetadataTests
{
    private static readonly JsonSerializerOptions Options = ContractJson.CreateOptions();

    [Fact]
    public void RecordsTheResolvedAudienceAndBothOutcomeFlags()
    {
        string json = JsonSerializer.Serialize(Metadata(), Options);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        Assert.Equal("2025-2026", root.GetProperty("academicYear").GetString());
        Assert.Equal(2, root.GetProperty("classYear").GetInt32());
        Assert.Equal("Turkish", root.GetProperty("programLanguage").GetString());
        Assert.Equal("C", root.GetProperty("selectors").GetProperty("practiceGroup").GetString());
        Assert.Equal("C1", root.GetProperty("selectors").GetProperty("practiceSubgroup").GetString());
        Assert.True(root.GetProperty("audienceChanged").GetBoolean());
        Assert.True(root.GetProperty("calendarResyncRequested").GetBoolean());
    }

    [Fact]
    public void NeverRecordsTheStudentNumber()
    {
        // The number identifies the person and answers nothing about which lessons the profile now
        // resolves, so it has no place in a log an operator reads to explain a calendar change.
        string json = JsonSerializer.Serialize(Metadata(), Options);

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.TryGetProperty("studentNumber", out _));
        Assert.DoesNotContain("2110101001", json, StringComparison.Ordinal);
    }

    [Fact]
    public void DistinguishesAnAudienceChangeThatQueuedNoResynchronization()
    {
        // A student who has not finished onboarding has no calendar to converge, so the audience
        // changed and nothing was queued. Recording only one flag would make this indistinguishable
        // from a change the audience rule does not read at all.
        ProfileUpdatedAuditMetadata metadata = Metadata() with
        {
            CalendarResyncRequested = false,
        };

        using JsonDocument document = JsonDocument.Parse(
            JsonSerializer.Serialize(metadata, Options));

        Assert.True(document.RootElement.GetProperty("audienceChanged").GetBoolean());
        Assert.False(document.RootElement.GetProperty("calendarResyncRequested").GetBoolean());
    }

    private static ProfileUpdatedAuditMetadata Metadata() => new()
    {
        AcademicYear = "2025-2026",
        ClassYear = 2,
        ProgramLanguage = "Turkish",
        Selectors = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["practiceGroup"] = "C",
            ["practiceSubgroup"] = "C1",
        },
        AudienceChanged = true,
        CalendarResyncRequested = true,
    };
}
