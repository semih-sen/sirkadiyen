using Sirkadiyen.Domain.Scheduling.Publication;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>
/// The predicate the pipeline uses to decide that a parse changed nothing.
/// </summary>
/// <remarks>
/// Two properties matter and they pull in opposite directions. It must ignore
/// everything that is not a schedule change, or the churn it exists to stop
/// comes straight back; and it must react to everything that is one, because a
/// hash that collides with a real change would silently stop publishing it.
/// The tests are written as that pair.
/// </remarks>
public sealed class CanonicalRecordSetHashTests
{
    private static readonly (string StableIdentity, string ContentHash)[] Empty = [];

    [Fact]
    public void TheSameRecordsInADifferentOrderHashTheSame()
    {
        // A parser is free to emit the same schedule in another order — a
        // worksheet read column-first instead of row-first produces exactly that
        // — and no lesson has moved.
        string first = CanonicalRecordSetHash.Compute(
        [
            ("identity-a", "content-a"),
            ("identity-b", "content-b"),
            ("identity-c", "content-c"),
        ]);
        string second = CanonicalRecordSetHash.Compute(
        [
            ("identity-c", "content-c"),
            ("identity-a", "content-a"),
            ("identity-b", "content-b"),
        ]);

        Assert.Equal(first, second);
        Assert.Equal(CanonicalRecordSetHash.Length, first.Length);
    }

    [Fact]
    public void ARecordWhoseContentMovedHashesDifferently()
    {
        // The lesson is the same lesson; it acquired a room, an instructor or a
        // cancellation. That is a change, and the diff must be allowed to see it.
        Assert.NotEqual(
            CanonicalRecordSetHash.Compute([("identity-a", "content-a")]),
            CanonicalRecordSetHash.Compute([("identity-a", "content-a-with-room")]));
    }

    [Fact]
    public void ARecordWhoseIdentityMovedHashesDifferently()
    {
        // A moved start time changes the stable identity while the content it
        // publishes may be unchanged, so the identity has to be hashed too.
        Assert.NotEqual(
            CanonicalRecordSetHash.Compute([("identity-a", "content-a")]),
            CanonicalRecordSetHash.Compute([("identity-b", "content-a")]));
    }

    [Fact]
    public void AddingOrRemovingARecordHashesDifferently()
    {
        string one = CanonicalRecordSetHash.Compute([("identity-a", "content-a")]);
        string two = CanonicalRecordSetHash.Compute(
        [
            ("identity-a", "content-a"),
            ("identity-b", "content-b"),
        ]);

        Assert.NotEqual(one, two);
    }

    [Fact]
    public void FieldsCannotRunTogetherIntoTheSameDigest()
    {
        // Without separators, ("ab", "c") and ("a", "bc") would concatenate into
        // the same bytes, and a cancelled lesson could hash as a scheduled one.
        Assert.NotEqual(
            CanonicalRecordSetHash.Compute([("ab", "c")]),
            CanonicalRecordSetHash.Compute([("a", "bc")]));
    }

    [Fact]
    public void RecordsCannotRunTogetherIntoTheSameDigest()
    {
        Assert.NotEqual(
            CanonicalRecordSetHash.Compute([("a", "b"), ("c", "d")]),
            CanonicalRecordSetHash.Compute([("a", "bcd")]));
    }

    [Fact]
    public void AnEmptySetHashesToAStableValue()
    {
        // An empty parse twice really is the same result twice. Whether an empty
        // revision may exist at all is validation's decision, not this one's.
        Assert.Equal(
            CanonicalRecordSetHash.Compute(Empty),
            CanonicalRecordSetHash.Compute(new (string, string)[0]));
        Assert.NotEqual(
            CanonicalRecordSetHash.Compute(Empty),
            CanonicalRecordSetHash.Compute([("identity-a", "content-a")]));
    }
}
