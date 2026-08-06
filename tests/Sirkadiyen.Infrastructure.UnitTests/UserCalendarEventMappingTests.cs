using Sirkadiyen.Domain.GoogleCalendar;
using Sirkadiyen.Domain.Scheduling.Sources;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

public sealed class UserCalendarEventMappingTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 24, 10, 0, 0, TimeSpan.Zero);

    private static readonly Guid UserId = Guid.CreateVersion7();

    private static readonly Guid RecordId = Guid.CreateVersion7();

    private static readonly SourceId Source = SourceId.Parse("G1-TR-ANNUAL");

    [Fact]
    public void CreateHoldsTheGivenValuesAndAssignsAnId()
    {
        UserCalendarEventMapping mapping = Create();

        Assert.NotEqual(Guid.Empty, mapping.Id);
        Assert.Equal(UserId, mapping.UserId);
        Assert.Equal("sha256:identity", mapping.StableIdentity);
        Assert.Equal(Source, mapping.SourceId);
        Assert.Equal(RecordId, mapping.CanonicalRecordId);
        Assert.Equal("calendar-id", mapping.GoogleCalendarId);
        Assert.Equal("event-id", mapping.GoogleEventId);
        Assert.Equal("sha256:content", mapping.ContentHash);
        Assert.Equal(Now, mapping.CreatedAtUtc);
        Assert.Equal(Now, mapping.UpdatedAtUtc);
    }

    [Fact]
    public void AMappingMustHaveAnOwner() =>
        Assert.Throws<ArgumentException>(() => Create(userId: Guid.Empty));

    [Fact]
    public void AMappingMustReferenceACanonicalRecord() =>
        Assert.Throws<ArgumentException>(() => Create(canonicalRecordId: Guid.Empty));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankRequiredValuesAreRejected(string blank)
    {
        Assert.Throws<ArgumentException>(() => Create(stableIdentity: blank));
        Assert.Throws<ArgumentException>(() => Create(googleCalendarId: blank));
        Assert.Throws<ArgumentException>(() => Create(googleEventId: blank));
        Assert.Throws<ArgumentException>(() => Create(contentHash: blank));
    }

    [Fact]
    public void AStableIdentityLongerThanTheBoundIsRejected()
    {
        string tooLong = new('x', UserCalendarEventMapping.MaximumStableIdentityLength + 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => Create(stableIdentity: tooLong));
    }

    [Fact]
    public void UpdatingContentChangesTheHashAndRecordButNotTheEvent()
    {
        UserCalendarEventMapping mapping = Create();
        Guid newerRecord = Guid.CreateVersion7();

        mapping.UpdateContent(newerRecord, "sha256:newer", Now.AddDays(1));

        Assert.Equal(newerRecord, mapping.CanonicalRecordId);
        Assert.Equal("sha256:newer", mapping.ContentHash);
        Assert.Equal(Now.AddDays(1), mapping.UpdatedAtUtc);

        // The link to the calendar event is durable; incremental sync patches it in place.
        Assert.Equal("event-id", mapping.GoogleEventId);
        Assert.Equal("sha256:identity", mapping.StableIdentity);
    }

    private static UserCalendarEventMapping Create(
        Guid? userId = null,
        string stableIdentity = "sha256:identity",
        Guid? canonicalRecordId = null,
        string googleCalendarId = "calendar-id",
        string googleEventId = "event-id",
        string contentHash = "sha256:content") =>
        UserCalendarEventMapping.Create(
            userId ?? UserId,
            stableIdentity,
            Source,
            canonicalRecordId ?? RecordId,
            googleCalendarId,
            googleEventId,
            contentHash,
            Now);
}
