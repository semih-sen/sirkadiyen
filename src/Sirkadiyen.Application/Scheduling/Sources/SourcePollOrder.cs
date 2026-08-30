using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Application.Scheduling.Sources;

/// <summary>
/// Orders a polling cycle so a companion is acquired before the sources that read
/// it (ADR-133).
/// </summary>
/// <remarks>
/// <para>
/// A parse run is keyed by the snapshot together with a fingerprint of its
/// companions' content, so a source re-parses when a companion changes. Which
/// cycle that happens in is decided by the order the sources are polled in, and
/// plain identifier order gets it backwards: every annual source sorts under
/// `G1`, `G2` or `G3` and the amphitheatre program it reads sorts under `S`. The
/// annual sources would be polled against last week's rooms, and the new ones
/// would not reach a calendar until the following cycle.
/// </para>
/// <para>
/// That is worst for the weekly amphitheatre program, which is only ever current
/// for five days, but it is the same for the Grade 3 bedside documents, which sort
/// after the annual workbooks that read them for the same reason.
/// </para>
/// <para>
/// Ordering is a stable topological sort: dependencies first, and identifier order
/// among sources that do not depend on each other, so a cycle that changes nothing
/// polls in exactly the same order as the one before it.
/// </para>
/// </remarks>
public static class SourcePollOrder
{
    /// <summary>
    /// The supplied sources, ordered so every companion precedes its readers.
    /// </summary>
    /// <remarks>
    /// A companion that is not itself in the list — not catalogued, or polling
    /// disabled — constrains nothing, because there is no cycle position to place
    /// it in. A source whose companions form a cycle cannot be ordered against
    /// them and is emitted after everything that could be ordered, in identifier
    /// order. That keeps a misconfiguration from dropping a source out of the
    /// cycle entirely, which would silently stop acquiring it.
    /// </remarks>
    public static IReadOnlyList<ScheduleSource> Arrange(IEnumerable<ScheduleSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        List<ScheduleSource> pending = [.. sources.OrderBy(
            static source => source.SourceId.Value,
            StringComparer.Ordinal)];

        HashSet<string> present = [.. pending.Select(static source => source.SourceId.Value)];
        HashSet<string> emitted = new(StringComparer.Ordinal);
        List<ScheduleSource> ordered = new(pending.Count);

        // Kahn's algorithm over the already-sorted list: repeatedly emit every
        // source whose companions have all been emitted. Scanning in identifier
        // order makes the result deterministic without a priority queue, and the
        // list is small enough that the repeated scan costs nothing.
        while (pending.Count > 0)
        {
            List<ScheduleSource> ready = [.. pending.Where(source => IsReady(source, present, emitted))];
            if (ready.Count == 0)
            {
                // Everything left depends on something else left: a companion
                // cycle. Emit the remainder rather than losing it.
                ordered.AddRange(pending);
                break;
            }

            foreach (ScheduleSource source in ready)
            {
                ordered.Add(source);
                emitted.Add(source.SourceId.Value);
            }

            pending.RemoveAll(source => emitted.Contains(source.SourceId.Value));
        }

        return ordered;
    }

    private static bool IsReady(
        ScheduleSource source,
        HashSet<string> present,
        HashSet<string> emitted) =>
        source.CompanionSourceIds.All(companion =>
            !present.Contains(companion.Value) || emitted.Contains(companion.Value));
}
