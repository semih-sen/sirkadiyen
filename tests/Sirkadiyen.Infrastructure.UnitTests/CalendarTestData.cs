using System.Text.Json;
using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Contracts.Serialization;
using Sirkadiyen.Domain.SchedulePublication;
using Sirkadiyen.Domain.ScheduleSources;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>Builders for the canonical records and profiles the calendar sync tests exercise.</summary>
internal static class CalendarTestData
{
    public const string AcademicYear = "2025-2026";

    public static StudentProfileView Profile(
        int classYear = 1,
        ProgramLanguage programLanguage = ProgramLanguage.Turkish,
        string academicYear = AcademicYear,
        IReadOnlyDictionary<string, string>? selectors = null) => new()
        {
            UserId = Guid.CreateVersion7(),
            AcademicYear = academicYear,
            ClassYear = classYear,
            ProgramLanguage = programLanguage,
            StudentNumber = "0101240048",
            SelectorSchemaVersion = "v1",
            Selectors = selectors ?? new Dictionary<string, string>(StringComparer.Ordinal),
            UpdatedAtUtc = DateTimeOffset.UnixEpoch,
        };

    public static CanonicalScheduleRecord Record(
        AudienceScope scope = AudienceScope.AllStudentsInProgram,
        IReadOnlyList<(string Dimension, string Value)>? selectors = null,
        int classYear = 1,
        ProgramLanguage programLanguage = ProgramLanguage.Turkish,
        string academicYear = AcademicYear,
        CanonicalRecordStatus status = CanonicalRecordStatus.Scheduled,
        bool allDay = false,
        DateOnly? date = null,
        TimeOnly? start = null,
        TimeOnly? end = null,
        string? stableIdentity = null,
        string displayTitle = "Lesson",
        string? instructor = null,
        string? location = null,
        SourceId? sourceId = null,
        string contentHash = "sha256:content")
    {
        string audienceJson = JsonSerializer.Serialize(
            (selectors ?? []).Select(selector => new
            {
                dimension = selector.Dimension,
                value = selector.Value,
            }),
            ContractJson.CreateOptions());

        DateOnly localDate = date ?? new DateOnly(2025, 10, 3);

        return new CanonicalScheduleRecord(
            Guid.CreateVersion7(),
            sourceId ?? SourceId.Parse("G1-TR-ANNUAL"),
            $"cand-{Guid.NewGuid():N}",
            status,
            academicYear,
            classYear,
            programLanguage,
            ScheduleEventType.Theory,
            scope,
            audienceJson,
            displayTitle,
            null,
            localDate,
            allDay ? null : start ?? new TimeOnly(9, 0),
            allDay ? null : end ?? new TimeOnly(10, 0),
            allDay,
            "Europe/Istanbul",
            stableIdentity ?? $"identity-{Guid.NewGuid():N}",
            contentHash,
            0.99m,
            "[]",
            instructor,
            location);
    }
}
