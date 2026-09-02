namespace Sirkadiyen.Application.Administration;

/// <summary>
/// The live view of the host's own CPU, memory and disk usage, backing the admin server dashboard's
/// resource panel. It is served from an in-process ring buffer that a background sampler fills at a
/// fixed cadence, so "now" and the 1-, 5- and 15-minute-ago readings are all real measurements taken
/// on this host — nothing is interpolated or synthesized (AI_GUIDELINE §19).
///
/// The reading is host-wide, not process-wide: CPU and memory come from the kernel's <c>/proc</c>
/// counters and disk from the mounted filesystems, so the numbers describe the whole Ubuntu host the
/// API runs on. Only Linux exposes these counters; on any other platform the snapshot reports itself
/// unavailable rather than inventing values.
/// </summary>
public interface IServerResourceMonitor
{
    ServerResourceSnapshot GetSnapshot();
}

public sealed record ServerResourceSnapshot
{
    public required DateTimeOffset GeneratedAtUtc { get; init; }

    /// <summary>
    /// False when the host is not Linux or its <c>/proc</c> counters are unreadable. Nothing but
    /// <see cref="UnavailableReason"/> is populated then, so the UI can say why rather than show zeros.
    /// </summary>
    public required bool IsAvailable { get; init; }

    public string? UnavailableReason { get; init; }

    /// <summary>Seconds between two consecutive samples, so the UI can state how fresh the history is.</summary>
    public required int SampleIntervalSeconds { get; init; }

    /// <summary>
    /// How many samples the ring buffer currently holds. It is small right after start-up, which is
    /// why the 5- and 15-minute readings can be empty until the buffer has filled.
    /// </summary>
    public required int RetainedSampleCount { get; init; }

    public required int ProcessorCount { get; init; }

    /// <summary>
    /// Kernel load averages over 1, 5 and 15 minutes (<c>/proc/loadavg</c>). Unlike the sampled
    /// readings these are the kernel's own moving averages, available immediately at start-up. Null
    /// when unreadable.
    /// </summary>
    public IReadOnlyList<double>? LoadAverages { get; init; }

    public long? MemoryTotalBytes { get; init; }

    /// <summary>
    /// The four readings in order: now, then the samples closest to 1, 5 and 15 minutes ago. A
    /// reading whose sample does not exist yet carries a null <see cref="ResourceReading.SampleAtUtc"/>
    /// and null values.
    /// </summary>
    public required IReadOnlyList<ResourceReading> Readings { get; init; }

    /// <summary>Current usage of each real (block-device backed) mounted filesystem.</summary>
    public required IReadOnlyList<DiskUsageView> Disks { get; init; }
}

public sealed record ResourceReading
{
    /// <summary>The point this reading targets: 0 (now), 60, 300 or 900 seconds ago.</summary>
    public required int TargetSecondsAgo { get; init; }

    /// <summary>When the backing sample was actually taken, or null when the buffer has no such sample.</summary>
    public DateTimeOffset? SampleAtUtc { get; init; }

    public double? CpuPercent { get; init; }

    public double? MemoryPercent { get; init; }

    public long? MemoryUsedBytes { get; init; }

    /// <summary>Usage of the primary (root <c>/</c>) filesystem at this reading's time.</summary>
    public double? DiskPercent { get; init; }
}

public sealed record DiskUsageView
{
    public required string MountPoint { get; init; }

    public required long TotalBytes { get; init; }

    public required long UsedBytes { get; init; }

    public required long AvailableBytes { get; init; }

    public required double UsedPercent { get; init; }
}
