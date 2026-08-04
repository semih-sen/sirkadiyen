using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Sirkadiyen.Domain.Finance;
using Sirkadiyen.Infrastructure.Persistence;
using Xunit;

namespace Sirkadiyen.Persistence.Tests;

/// <summary>
/// Asserts the finance model's mapping without a database, so a lost index or a dropped unique
/// constraint fails the ordinary test run (docs/database.md).
/// </summary>
public sealed class FinanceModelTests
{
    private static readonly SirkadiyenDbContext Context = new(
        new DbContextOptionsBuilder<SirkadiyenDbContext>()
            .UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused")
            .Options);

    [Theory]
    [InlineData(typeof(FinanceTransaction), nameof(FinanceTransaction.Amount))]
    [InlineData(typeof(FinanceLedgerEntry), nameof(FinanceLedgerEntry.Amount))]
    [InlineData(typeof(FinanceAudit), nameof(FinanceAudit.AmountDelta))]
    public void MoneyPropertiesAreMappedNumeric18Comma2(Type entityType, string propertyName)
    {
        IEntityType entity = Context.Model.FindEntityType(entityType)!;
        IProperty property = entity.FindProperty(propertyName)!;

        Assert.Equal(18, property.GetPrecision());
        Assert.Equal(2, property.GetScale());
    }

    [Fact]
    public void EnumsAreStoredAsStrings()
    {
        IEntityType transaction = Context.Model.FindEntityType(typeof(FinanceTransaction))!;
        IEntityType entry = Context.Model.FindEntityType(typeof(FinanceLedgerEntry))!;

        Assert.Equal(typeof(string), transaction.FindProperty(nameof(FinanceTransaction.Kind))!.GetProviderClrType());
        Assert.Equal(typeof(string), entry.FindProperty(nameof(FinanceLedgerEntry.Leg))!.GetProviderClrType());
    }

    [Fact]
    public void ADisplayNameIsUniquePerHolder()
    {
        IEntityType holder = Context.Model.FindEntityType(typeof(FinanceAccountHolder))!;
        IIndex index = Assert.Single(
            holder.GetIndexes(),
            candidate => candidate.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(FinanceAccountHolder.DisplayName)]));

        Assert.True(index.IsUnique);
    }

    [Fact]
    public void AUserIsLinkedToAtMostOneHolder()
    {
        IEntityType holder = Context.Model.FindEntityType(typeof(FinanceAccountHolder))!;
        IIndex index = Assert.Single(
            holder.GetIndexes(),
            candidate => candidate.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(FinanceAccountHolder.UserId)]));

        Assert.True(index.IsUnique);
        Assert.Equal("\"UserId\" IS NOT NULL", index.GetFilter());
    }

    [Fact]
    public void AnAccountNameIsUniquePerHolder()
    {
        IEntityType account = Context.Model.FindEntityType(typeof(FinanceAccount))!;
        IIndex index = Assert.Single(
            account.GetIndexes(),
            candidate => candidate.Properties.Select(property => property.Name)
                .SequenceEqual(
                [
                    nameof(FinanceAccount.FinanceAccountHolderId),
                    nameof(FinanceAccount.Name),
                ]));

        Assert.True(index.IsUnique);
    }

    [Fact]
    public void ATransactionHasAtMostOneLegOfEachKind()
    {
        IEntityType entry = Context.Model.FindEntityType(typeof(FinanceLedgerEntry))!;
        IIndex index = Assert.Single(
            entry.GetIndexes(),
            candidate => candidate.Properties.Select(property => property.Name)
                .SequenceEqual(
                [
                    nameof(FinanceLedgerEntry.FinanceTransactionId),
                    nameof(FinanceLedgerEntry.Leg),
                ]));

        Assert.True(index.IsUnique);
    }

    [Fact]
    public void ASelfTransferIsImpossibleAtTheSchemaLevel()
    {
        IEntityType entry = Context.Model.FindEntityType(typeof(FinanceLedgerEntry))!;
        IIndex index = Assert.Single(
            entry.GetIndexes(),
            candidate => candidate.Properties.Select(property => property.Name)
                .SequenceEqual(
                [
                    nameof(FinanceLedgerEntry.FinanceTransactionId),
                    nameof(FinanceLedgerEntry.FinanceAccountId),
                ]));

        Assert.True(index.IsUnique);
    }

    [Fact]
    public void AnAccountHasAtMostOneOpeningBalance()
    {
        IEntityType entry = Context.Model.FindEntityType(typeof(FinanceLedgerEntry))!;
        IIndex index = Assert.Single(
            entry.GetIndexes(),
            candidate => candidate.Properties.Select(property => property.Name)
                .SequenceEqual(
                [
                    nameof(FinanceLedgerEntry.FinanceAccountId),
                    nameof(FinanceLedgerEntry.Kind),
                ]));

        Assert.True(index.IsUnique);
        Assert.Equal("\"Kind\" = 'OpeningBalance'", index.GetFilter());
    }

    [Fact]
    public void TheLedgerHoldingForeignKeysAreAllRestrict()
    {
        IEntityType entry = Context.Model.FindEntityType(typeof(FinanceLedgerEntry))!;

        Assert.All(
            entry.GetForeignKeys(),
            foreignKey => Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior));
    }

    [Fact]
    public void TheFinanceLedgerHasItsOwnAdditiveMigration()
    {
        Assert.Contains(
            Context.Database.GetMigrations(),
            migration => migration.Contains("AddFinanceLedger", StringComparison.Ordinal));
    }
}
