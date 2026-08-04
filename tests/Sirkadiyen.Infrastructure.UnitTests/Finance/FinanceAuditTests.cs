using Sirkadiyen.Domain.Finance;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests.Finance;

public sealed class FinanceAuditTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 23, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateAllowsAMissingReasonForACreationAction()
    {
        FinanceAudit audit = FinanceAudit.Create(
            FinanceAuditAction.TransactionCreated,
            "FinanceTransaction",
            Guid.NewGuid(),
            Guid.NewGuid(),
            "admin@example.com",
            Now,
            correlationId: null,
            reason: null,
            beforeState: null,
            afterState: "{}",
            changedFields: null,
            amountDelta: 100m,
            revisionNumber: 1);

        Assert.Null(audit.Reason);
    }

    [Fact]
    public void CreateRequiresAReasonForAnUpdateAction()
    {
        Assert.Throws<ArgumentException>(() => FinanceAudit.Create(
            FinanceAuditAction.TransactionUpdated,
            "FinanceTransaction",
            Guid.NewGuid(),
            Guid.NewGuid(),
            "admin@example.com",
            Now,
            correlationId: null,
            reason: "   ",
            beforeState: "{}",
            afterState: "{}",
            changedFields: null,
            amountDelta: 0m,
            revisionNumber: 2));
    }

    [Fact]
    public void CreateRequiresANonNullReasonForAnUpdateAction()
    {
        Assert.Throws<ArgumentNullException>(() => FinanceAudit.Create(
            FinanceAuditAction.TransactionUpdated,
            "FinanceTransaction",
            Guid.NewGuid(),
            Guid.NewGuid(),
            "admin@example.com",
            Now,
            correlationId: null,
            reason: null,
            beforeState: "{}",
            afterState: "{}",
            changedFields: null,
            amountDelta: 0m,
            revisionNumber: 2));
    }

    [Fact]
    public void CreateRequiresAReasonForADeleteAction()
    {
        Assert.Throws<ArgumentException>(() => FinanceAudit.Create(
            FinanceAuditAction.TransactionDeleted,
            "FinanceTransaction",
            Guid.NewGuid(),
            Guid.NewGuid(),
            "admin@example.com",
            Now,
            correlationId: null,
            reason: "   ",
            beforeState: "{}",
            afterState: null,
            changedFields: null,
            amountDelta: -100m,
            revisionNumber: 1));
    }

    [Fact]
    public void ChangedFieldsPreservesTheGivenOrder()
    {
        FinanceAudit audit = FinanceAudit.Create(
            FinanceAuditAction.TransactionUpdated,
            "FinanceTransaction",
            Guid.NewGuid(),
            Guid.NewGuid(),
            "admin@example.com",
            Now,
            correlationId: "corr-1",
            reason: "Fixed a typo in the amount.",
            beforeState: "{}",
            afterState: "{}",
            changedFields: ["Amount", "Description"],
            amountDelta: 50m,
            revisionNumber: 2);

        Assert.Equal(["Amount", "Description"], audit.ChangedFields);
    }

    [Fact]
    public void CreateRejectsARevisionNumberBelowOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FinanceAudit.Create(
            FinanceAuditAction.TransactionCreated,
            "FinanceTransaction",
            Guid.NewGuid(),
            Guid.NewGuid(),
            "admin@example.com",
            Now,
            correlationId: null,
            reason: null,
            beforeState: null,
            afterState: "{}",
            changedFields: null,
            amountDelta: 100m,
            revisionNumber: 0));
    }
}
