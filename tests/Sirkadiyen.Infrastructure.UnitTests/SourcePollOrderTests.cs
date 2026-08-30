using Sirkadiyen.Application.Scheduling.Sources;
using Sirkadiyen.Domain.Scheduling.Sources;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>
/// Tests for polling a companion before the sources that read it (ADR-133).
/// </summary>
public sealed class SourcePollOrderTests
{
    [Fact]
    public void ACompanionIsPolledBeforeTheSourcesThatReadIt()
    {
        // This is the real catalog's shape, and the reason the rule exists: every
        // annual source sorts under G and the amphitheatre program it reads sorts
        // under S, so identifier order polls them in exactly the wrong sequence.
        IReadOnlyList<ScheduleSource> ordered = SourcePollOrder.Arrange(
        [
            Source("G1-TR-ANNUAL", "SHARED-AMPHI"),
            Source("G3-TR-A-ANNUAL", "G3-TR-A-BEDSIDE", "SHARED-AMPHI"),
            Source("G3-TR-A-BEDSIDE"),
            Source("SHARED-AMPHI"),
        ]);

        Assert.Equal(
            ["G3-TR-A-BEDSIDE", "SHARED-AMPHI", "G1-TR-ANNUAL", "G3-TR-A-ANNUAL"],
            Ids(ordered));
    }

    [Fact]
    public void SourcesThatDependOnNothingKeepIdentifierOrder()
    {
        IReadOnlyList<ScheduleSource> ordered = SourcePollOrder.Arrange(
            [Source("G2-TR-ANNUAL"), Source("G1-TR-ANNUAL"), Source("SHARED-AMPHI")]);

        Assert.Equal(["G1-TR-ANNUAL", "G2-TR-ANNUAL", "SHARED-AMPHI"], Ids(ordered));
    }

    [Fact]
    public void TheOrderIsStableAcrossCyclesWhateverOrderTheStoreReturns()
    {
        // An unstable order would acquire and parse in a different sequence every
        // cycle, which makes an incident impossible to read back.
        ScheduleSource[] sources =
        [
            Source("G1-TR-ANNUAL", "SHARED-AMPHI"),
            Source("G2-TR-ANNUAL", "SHARED-AMPHI"),
            Source("SHARED-AMPHI"),
        ];

        Assert.Equal(
            Ids(SourcePollOrder.Arrange(sources)),
            Ids(SourcePollOrder.Arrange(sources.Reverse())));
    }

    [Fact]
    public void ACompanionThatIsNotBeingPolledConstrainsNothing()
    {
        // Polling may be disabled on a companion, or it may not be catalogued yet.
        // Neither is a reason to hold back the source that would have read it.
        IReadOnlyList<ScheduleSource> ordered = SourcePollOrder.Arrange(
            [Source("G1-TR-ANNUAL", "SHARED-AMPHI")]);

        Assert.Equal(["G1-TR-ANNUAL"], Ids(ordered));
    }

    [Fact]
    public void ACompanionCycleStillPollsEverySource()
    {
        // A misconfiguration must not silently drop a source out of the cycle,
        // which would stop acquiring it altogether.
        IReadOnlyList<ScheduleSource> ordered = SourcePollOrder.Arrange(
        [
            Source("A-SOURCE", "B-SOURCE"),
            Source("B-SOURCE", "A-SOURCE"),
            Source("C-SOURCE"),
        ]);

        Assert.Equal(["C-SOURCE", "A-SOURCE", "B-SOURCE"], Ids(ordered));
    }

    [Fact]
    public void ACompanionOfACompanionIsPolledFirst()
    {
        IReadOnlyList<ScheduleSource> ordered = SourcePollOrder.Arrange(
        [
            Source("A-READER", "B-MIDDLE"),
            Source("B-MIDDLE", "C-ROOT"),
            Source("C-ROOT"),
        ]);

        Assert.Equal(["C-ROOT", "B-MIDDLE", "A-READER"], Ids(ordered));
    }

    private static string[] Ids(IEnumerable<ScheduleSource> sources) =>
        [.. sources.Select(static source => source.SourceId.Value)];

    private static ScheduleSource Source(string sourceId, params string[] companions) => new(
        SourceId.Parse(sourceId),
        sourceId,
        ScheduleSourceTransport.GoogleSheets,
        ScheduleDocumentFormat.GoogleSheet,
        $"https://docs.google.com/spreadsheets/d/{sourceId}",
        "grade1_yearly_v1",
        "1.7.0",
        "2026-2027",
        1,
        ProgramLanguage.Turkish,
        "Europe/Istanbul",
        externalId: sourceId,
        sheetGid: 0,
        companionSourceIds: [.. companions.Select(SourceId.Parse)]);
}
