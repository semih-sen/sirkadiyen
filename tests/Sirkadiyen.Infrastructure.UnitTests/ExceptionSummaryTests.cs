using Sirkadiyen.Application.Common;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>
/// How a failure reads once its wrapping layers are peeled back.
/// </summary>
public sealed class ExceptionSummaryTests
{
    [Fact]
    public void TheInnerCauseIsShownNotTheGenericWrapper()
    {
        // The exact shape a failed parse or validation hits: EF's own message names no cause, and the
        // reason is one level down. Reporting only the outer message is what left the panel showing
        // "Failed" with nothing to fix.
        var inner = new InvalidOperationException(
            "23505: duplicate key value violates unique constraint \"ix_records_identity\"");
        var wrapper = new InvalidOperationException(
            "An error occurred while saving the entity changes. See the inner exception for details.",
            inner);

        string summary = ExceptionSummary.Describe(wrapper);

        Assert.Contains("duplicate key value violates unique constraint", summary);
        Assert.Contains("InvalidOperationException", summary);
    }

    [Fact]
    public void ARepeatedMessageIsNotDuplicatedAcrossLevels()
    {
        var inner = new InvalidOperationException("value too long for type character varying(64)");
        var wrapper = new InvalidOperationException(inner.Message, inner);

        string summary = ExceptionSummary.Describe(wrapper);

        Assert.Equal(
            "InvalidOperationException: value too long for type character varying(64)",
            summary);
    }

    [Fact]
    public void AnExceptionWithNoUsefulMessageStillNamesItsType()
    {
        string summary = ExceptionSummary.Describe(new EmptyMessageException());

        Assert.Equal(nameof(EmptyMessageException), summary);
    }

    [Fact]
    public void TheSummaryIsCappedToTheRequestedLength()
    {
        string summary = ExceptionSummary.Describe(
            new InvalidOperationException(new string('x', 5000)),
            maxLength: 200);

        Assert.Equal(200, summary.Length);
    }

    private sealed class EmptyMessageException : Exception
    {
        public override string Message => string.Empty;
    }
}
