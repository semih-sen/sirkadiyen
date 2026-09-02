using Sirkadiyen.Infrastructure.Observability;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>
/// The pure /proc parsers behind the admin server-resources panel. They run on any platform (they only
/// parse text), so the host-gated sampling is not exercised here — the parsing is.
/// </summary>
public sealed class ServerResourceMonitorTests
{
    [Fact]
    public void ParsesTheAggregateCpuLineIntoIdleAndTotalJiffies()
    {
        // cpu  user nice system idle iowait irq softirq steal guest guest_nice
        const string content =
            "cpu  100 20 30 800 40 0 10 0 0 0\n" +
            "cpu0 50 10 15 400 20 0 5 0 0 0\n" +
            "intr 12345\n";

        bool parsed = ServerResourceMonitor.TryParseProcStatCpu(content, out ulong idle, out ulong total);

        Assert.True(parsed);
        // idle + iowait = 800 + 40.
        Assert.Equal(840ul, idle);
        // sum of every field on the cpu line: 100+20+30+800+40+0+10 = 1000.
        Assert.Equal(1000ul, total);
    }

    [Fact]
    public void RejectsProcStatWithoutAnAggregateCpuLine()
    {
        Assert.False(ServerResourceMonitor.TryParseProcStatCpu("intr 1\nctxt 2\n", out _, out _));
    }

    [Fact]
    public void ParsesMemTotalAndMemAvailableIntoBytes()
    {
        const string content =
            "MemTotal:       16384000 kB\n" +
            "MemFree:         2000000 kB\n" +
            "MemAvailable:    8192000 kB\n" +
            "Buffers:          100000 kB\n";

        bool parsed = ServerResourceMonitor.TryParseMemInfo(content, out long totalBytes, out long availableBytes);

        Assert.True(parsed);
        Assert.Equal(16384000L * 1024, totalBytes);
        Assert.Equal(8192000L * 1024, availableBytes);
    }

    [Fact]
    public void RejectsMemInfoMissingMemAvailable()
    {
        Assert.False(ServerResourceMonitor.TryParseMemInfo("MemTotal: 16384000 kB\n", out _, out _));
    }

    [Fact]
    public void ParsesTheThreeLoadAverages()
    {
        bool parsed = ServerResourceMonitor.TryParseLoadAvg("0.52 0.41 0.33 1/512 12345\n", out double[] loads);

        Assert.True(parsed);
        Assert.Equal([0.52, 0.41, 0.33], loads);
    }

    [Fact]
    public void RejectsLoadAvgWithTooFewFields()
    {
        Assert.False(ServerResourceMonitor.TryParseLoadAvg("0.52 0.41\n", out _));
    }

    [Fact]
    public void SnapshotIsUnavailableOnANonLinuxHost()
    {
        // The parsers run anywhere; the live snapshot only does on Linux. On any other CI host the
        // monitor must say so rather than return zeros.
        if (OperatingSystem.IsLinux())
        {
            return;
        }

        ServerResourceMonitor monitor = new(TimeProvider.System, new ServerResourceMonitorOptions());

        ServerResourceSnapshot snapshot = monitor.GetSnapshot();

        Assert.False(snapshot.IsAvailable);
        Assert.NotNull(snapshot.UnavailableReason);
        Assert.Empty(snapshot.Readings);
        Assert.Empty(snapshot.Disks);
    }
}
