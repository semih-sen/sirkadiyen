using System.Globalization;
using System.Runtime.InteropServices;
using Sirkadiyen.Application.Administration;

namespace Sirkadiyen.Infrastructure.Observability;

public sealed record ServerResourceMonitorOptions
{
    /// <summary>Seconds between two samples. Small enough that "now" is fresh, large enough to stay cheap.</summary>
    public int SampleIntervalSeconds { get; init; } = 10;

    /// <summary>
    /// How far back the ring buffer keeps samples. It must exceed the longest reading (15 minutes) with
    /// room to spare so the 15-minute-ago sample is always inside the retained window once filled.
    /// </summary>
    public int RetentionMinutes { get; init; } = 17;
}

/// <summary>
/// Reads this Linux host's CPU, memory and disk usage from <c>/proc</c> and the mounted filesystems,
/// keeping a short in-process history so the admin dashboard can show "now" alongside the 1-, 5- and
/// 15-minute-ago readings. A single instance is shared: the background sampling service calls
/// <see cref="Sample"/> on a fixed cadence to append to the ring buffer, and the endpoint calls
/// <see cref="GetSnapshot"/> to read it. Both are safe to call concurrently.
///
/// The numbers are host-wide, taken from the kernel's counters rather than from this process, so they
/// describe the whole server. On a non-Linux host (or where <c>/proc</c> cannot be read) the monitor
/// reports itself unavailable instead of returning zeros (AI_GUIDELINE §19).
/// </summary>
public sealed class ServerResourceMonitor : IServerResourceMonitor
{
    private readonly TimeProvider _timeProvider;
    private readonly int _sampleIntervalSeconds;
    private readonly int _capacity;
    private readonly bool _isLinux;
    private readonly Lock _gate = new();
    private readonly Queue<Sample> _samples = new();

    public ServerResourceMonitor(TimeProvider timeProvider, ServerResourceMonitorOptions options)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);

        _timeProvider = timeProvider;
        _sampleIntervalSeconds = Math.Max(1, options.SampleIntervalSeconds);
        // One extra slot beyond the retention window so trimming never drops a sample still in range.
        _capacity = (options.RetentionMinutes * 60 / _sampleIntervalSeconds) + 2;
        _isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    }

    public int SampleIntervalSeconds => _sampleIntervalSeconds;

    /// <summary>
    /// Reads the counters once and appends a sample. The CPU figure is the busy fraction over the
    /// interval since the previous sample, so the first sample after start-up carries no CPU value.
    /// Never throws: an unreadable counter simply skips this tick.
    /// </summary>
    public void Sample()
    {
        if (!_isLinux)
        {
            return;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();

        if (!TryReadCpuTimes(out ulong idle, out ulong total)
            || !TryReadMemory(out long memoryTotalBytes, out long memoryAvailableBytes))
        {
            return;
        }

        long memoryUsedBytes = Math.Max(0, memoryTotalBytes - memoryAvailableBytes);
        double memoryPercent = memoryTotalBytes > 0
            ? 100d * memoryUsedBytes / memoryTotalBytes
            : 0d;
        double? rootDiskPercent = TryReadRootDiskPercent(out double disk) ? disk : null;

        lock (_gate)
        {
            double? cpuPercent = _samples.Count > 0
                ? ComputeCpuPercent(_samples.Last(), idle, total)
                : null;

            _samples.Enqueue(new Sample(now, idle, total, cpuPercent, memoryPercent, memoryUsedBytes, rootDiskPercent));
            while (_samples.Count > _capacity)
            {
                _samples.Dequeue();
            }
        }
    }

    public ServerResourceSnapshot GetSnapshot()
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();

        if (!_isLinux)
        {
            return Unavailable(now, "Sunucu kaynak sayaçları yalnızca Linux (/proc) üzerinde okunur.");
        }

        Sample[] samples;
        lock (_gate)
        {
            samples = [.. _samples];
        }

        bool memoryRead = TryReadMemory(out long memoryTotalBytes, out long memoryAvailableBytes);
        long? memoryTotal = memoryRead ? memoryTotalBytes : null;

        // "Now" is read live rather than taken from the newest sample, so a manual refresh is never
        // staler than the moment it is pressed. CPU still needs a baseline to difference against.
        double? liveMemoryPercent = null;
        long? liveMemoryUsed = null;
        if (memoryRead)
        {
            liveMemoryUsed = Math.Max(0, memoryTotalBytes - memoryAvailableBytes);
            liveMemoryPercent = memoryTotalBytes > 0 ? 100d * liveMemoryUsed / memoryTotalBytes : 0d;
        }

        double? liveCpuPercent = null;
        if (samples.Length > 0 && TryReadCpuTimes(out ulong idle, out ulong total))
        {
            liveCpuPercent = ComputeCpuPercent(samples[^1], idle, total);
        }

        double? liveDiskPercent = TryReadRootDiskPercent(out double rootDisk) ? rootDisk : null;

        List<ResourceReading> readings =
        [
            new ResourceReading
            {
                TargetSecondsAgo = 0,
                SampleAtUtc = samples.Length > 0 ? now : null,
                CpuPercent = Round(liveCpuPercent),
                MemoryPercent = Round(liveMemoryPercent),
                MemoryUsedBytes = liveMemoryUsed,
                DiskPercent = Round(liveDiskPercent),
            },
            HistoricalReading(samples, now, 60),
            HistoricalReading(samples, now, 300),
            HistoricalReading(samples, now, 900),
        ];

        return new ServerResourceSnapshot
        {
            GeneratedAtUtc = now,
            IsAvailable = true,
            SampleIntervalSeconds = _sampleIntervalSeconds,
            RetainedSampleCount = samples.Length,
            ProcessorCount = Environment.ProcessorCount,
            LoadAverages = TryReadLoadAverages(out double[] loads) ? loads : null,
            MemoryTotalBytes = memoryTotal,
            Readings = readings,
            Disks = ReadDisks(),
        };
    }

    /// <summary>Picks the buffered sample nearest the target age, or an empty reading when none is close enough.</summary>
    private ResourceReading HistoricalReading(Sample[] samples, DateTimeOffset now, int secondsAgo)
    {
        DateTimeOffset target = now.AddSeconds(-secondsAgo);
        // A nearest sample beyond this distance means the buffer does not yet span the target age; the
        // reading stays empty rather than passing off a much newer sample as "15 minutes ago".
        double toleranceSeconds = _sampleIntervalSeconds;

        Sample? nearest = null;
        double bestDistance = double.MaxValue;
        foreach (Sample sample in samples)
        {
            double distance = Math.Abs((sample.At - target).TotalSeconds);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                nearest = sample;
            }
        }

        if (nearest is not { } chosen || bestDistance > toleranceSeconds)
        {
            return new ResourceReading { TargetSecondsAgo = secondsAgo };
        }

        return new ResourceReading
        {
            TargetSecondsAgo = secondsAgo,
            SampleAtUtc = chosen.At,
            CpuPercent = Round(chosen.CpuPercent),
            MemoryPercent = Round(chosen.MemoryPercent),
            MemoryUsedBytes = chosen.MemoryUsedBytes,
            DiskPercent = Round(chosen.RootDiskPercent),
        };
    }

    private ServerResourceSnapshot Unavailable(DateTimeOffset now, string reason) => new()
    {
        GeneratedAtUtc = now,
        IsAvailable = false,
        UnavailableReason = reason,
        SampleIntervalSeconds = _sampleIntervalSeconds,
        RetainedSampleCount = 0,
        ProcessorCount = Environment.ProcessorCount,
        Readings = [],
        Disks = [],
    };

    private static double ComputeCpuPercent(Sample previous, ulong idle, ulong total)
    {
        double totalDelta = total >= previous.CpuTotal ? total - previous.CpuTotal : 0d;
        double idleDelta = idle >= previous.CpuIdle ? idle - previous.CpuIdle : 0d;
        if (totalDelta <= 0d)
        {
            return 0d;
        }

        double busy = 100d * (totalDelta - idleDelta) / totalDelta;
        return Math.Clamp(busy, 0d, 100d);
    }

    private static double? Round(double? value) =>
        value is { } v ? Math.Round(v, 1, MidpointRounding.AwayFromZero) : null;

    // ---- /proc readers --------------------------------------------------------

    private static bool TryReadCpuTimes(out ulong idle, out ulong total)
    {
        idle = 0;
        total = 0;
        return TryReadFile("/proc/stat", out string content) && TryParseProcStatCpu(content, out idle, out total);
    }

    private static bool TryReadMemory(out long totalBytes, out long availableBytes)
    {
        totalBytes = 0;
        availableBytes = 0;
        return TryReadFile("/proc/meminfo", out string content) && TryParseMemInfo(content, out totalBytes, out availableBytes);
    }

    private static bool TryReadLoadAverages(out double[] loads)
    {
        loads = [];
        return TryReadFile("/proc/loadavg", out string content) && TryParseLoadAvg(content, out loads);
    }

    /// <summary>
    /// Parses the aggregate <c>cpu</c> line of <c>/proc/stat</c> into its idle and total jiffies.
    /// Idle counts idle plus iowait, matching how utilization is conventionally computed.
    /// </summary>
    public static bool TryParseProcStatCpu(string content, out ulong idle, out ulong total)
    {
        idle = 0;
        total = 0;
        ArgumentNullException.ThrowIfNull(content);

        foreach (string rawLine in content.Split('\n'))
        {
            string line = rawLine.Trim();
            if (!line.StartsWith("cpu ", StringComparison.Ordinal))
            {
                continue;
            }

            string[] fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // fields[0] == "cpu"; then user nice system idle iowait irq softirq steal guest guest_nice.
            ulong sum = 0;
            ulong idleTime = 0;
            for (int i = 1; i < fields.Length; i++)
            {
                if (!ulong.TryParse(fields[i], NumberStyles.None, CultureInfo.InvariantCulture, out ulong value))
                {
                    return false;
                }

                sum += value;
                if (i is 4 or 5)
                {
                    idleTime += value;
                }
            }

            if (fields.Length < 5)
            {
                return false;
            }

            idle = idleTime;
            total = sum;
            return true;
        }

        return false;
    }

    /// <summary>Parses <c>MemTotal</c> and <c>MemAvailable</c> (both kB) from <c>/proc/meminfo</c> into bytes.</summary>
    public static bool TryParseMemInfo(string content, out long totalBytes, out long availableBytes)
    {
        totalBytes = 0;
        availableBytes = 0;
        ArgumentNullException.ThrowIfNull(content);

        long? total = null;
        long? available = null;
        foreach (string rawLine in content.Split('\n'))
        {
            if (TryReadMemInfoKilobytes(rawLine, "MemTotal:", out long totalKb))
            {
                total = totalKb;
            }
            else if (TryReadMemInfoKilobytes(rawLine, "MemAvailable:", out long availableKb))
            {
                available = availableKb;
            }
        }

        if (total is not { } t || available is not { } a)
        {
            return false;
        }

        totalBytes = t * 1024;
        availableBytes = a * 1024;
        return true;
    }

    private static bool TryReadMemInfoKilobytes(string line, string label, out long kilobytes)
    {
        kilobytes = 0;
        string trimmed = line.Trim();
        if (!trimmed.StartsWith(label, StringComparison.Ordinal))
        {
            return false;
        }

        string[] fields = trimmed[label.Length..]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return fields.Length > 0
            && long.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out kilobytes);
    }

    /// <summary>Parses the three load averages (1, 5, 15 minutes) from <c>/proc/loadavg</c>.</summary>
    public static bool TryParseLoadAvg(string content, out double[] loads)
    {
        loads = [];
        ArgumentNullException.ThrowIfNull(content);

        string[] fields = content.Split([' ', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 3)
        {
            return false;
        }

        double[] parsed = new double[3];
        for (int i = 0; i < 3; i++)
        {
            if (!double.TryParse(fields[i], NumberStyles.Float, CultureInfo.InvariantCulture, out parsed[i]))
            {
                return false;
            }
        }

        loads = parsed;
        return true;
    }

    private static bool TryReadRootDiskPercent(out double usedPercent)
    {
        usedPercent = 0;
        try
        {
            DriveInfo root = new("/");
            if (!root.IsReady || root.TotalSize <= 0)
            {
                return false;
            }

            long used = root.TotalSize - root.TotalFreeSpace;
            usedPercent = Math.Clamp(100d * used / root.TotalSize, 0d, 100d);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// The real, block-device backed filesystems, read from <c>/proc/mounts</c>. Pseudo and virtual
    /// mounts (tmpfs, overlay, cgroup) and read-only snap squashfs images are left out so the table
    /// shows the disks an operator actually manages.
    /// </summary>
    private static IReadOnlyList<DiskUsageView> ReadDisks()
    {
        if (!TryReadFile("/proc/mounts", out string content))
        {
            return [];
        }

        List<DiskUsageView> disks = [];
        HashSet<string> seenMounts = [];
        foreach (string rawLine in content.Split('\n'))
        {
            string[] fields = rawLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 3)
            {
                continue;
            }

            string device = fields[0];
            string mountPoint = fields[1].Replace("\\040", " ", StringComparison.Ordinal);
            string fileSystemType = fields[2];

            if (!device.StartsWith("/dev/", StringComparison.Ordinal)
                || device.StartsWith("/dev/loop", StringComparison.Ordinal)
                || string.Equals(fileSystemType, "squashfs", StringComparison.Ordinal)
                || !seenMounts.Add(mountPoint))
            {
                continue;
            }

            try
            {
                DriveInfo drive = new(mountPoint);
                if (!drive.IsReady || drive.TotalSize <= 0)
                {
                    continue;
                }

                long used = drive.TotalSize - drive.TotalFreeSpace;
                disks.Add(new DiskUsageView
                {
                    MountPoint = mountPoint,
                    TotalBytes = drive.TotalSize,
                    UsedBytes = Math.Max(0, used),
                    AvailableBytes = drive.AvailableFreeSpace,
                    UsedPercent = Math.Round(Math.Clamp(100d * used / drive.TotalSize, 0d, 100d), 1, MidpointRounding.AwayFromZero),
                });
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // A mount that disappeared between reading /proc/mounts and stat-ing it is simply skipped.
            }
        }

        return disks;
    }

    private static bool TryReadFile(string path, out string content)
    {
        content = string.Empty;
        try
        {
            content = File.ReadAllText(path);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private readonly record struct Sample(
        DateTimeOffset At,
        ulong CpuIdle,
        ulong CpuTotal,
        double? CpuPercent,
        double MemoryPercent,
        long MemoryUsedBytes,
        double? RootDiskPercent);
}
