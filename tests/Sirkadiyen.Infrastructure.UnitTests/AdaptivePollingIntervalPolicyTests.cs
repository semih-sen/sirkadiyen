using Sirkadiyen.Application.Scheduling.Ingestion;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

public sealed class AdaptivePollingIntervalPolicyTests
{
    private readonly AdaptivePollingIntervalPolicy policy = new(new AdaptivePollingOptions());

    [Theory]
    [InlineData("2026-07-20T03:59:00+00:00", 45)] // Monday 06:59 Istanbul
    [InlineData("2026-07-20T04:00:00+00:00", 15)] // Monday 07:00 Istanbul
    [InlineData("2026-07-20T12:59:00+00:00", 15)] // Monday 15:59 Istanbul
    [InlineData("2026-07-20T13:00:00+00:00", 25)] // Monday 16:00 Istanbul
    [InlineData("2026-07-20T17:59:00+00:00", 25)] // Monday 20:59 Istanbul
    [InlineData("2026-07-20T18:00:00+00:00", 45)] // Monday 21:00 Istanbul
    [InlineData("2026-07-25T09:00:00+00:00", 60)] // Saturday
    [InlineData("2026-07-26T09:00:00+00:00", 60)] // Sunday
    public void SelectsTheConfiguredIstanbulWindow(string utcTimestamp, int expectedMinutes)
    {
        TimeSpan interval = policy.GetInterval(DateTimeOffset.Parse(utcTimestamp));

        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), interval);
    }

    [Fact]
    public void RejectsOverlappingOrReversedWindows()
    {
        AdaptivePollingOptions options = new()
        {
            DaytimeStart = new TimeOnly(17, 0),
            LateAfternoonStart = new TimeOnly(16, 0),
        };

        Assert.Throws<InvalidOperationException>(() => new AdaptivePollingIntervalPolicy(options));
    }
}
