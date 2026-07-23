using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Sirkadiyen.Domain.Licensing;
using Sirkadiyen.Infrastructure.Persistence;
using Xunit;

namespace Sirkadiyen.Persistence.Tests;

public sealed class LicenseModelTests
{
    private static readonly SirkadiyenDbContext Context = new(
        new DbContextOptionsBuilder<SirkadiyenDbContext>()
            .UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused")
            .Options);

    [Fact]
    public void ManualActivationHasAnExplicitKindAndNoRequiredCodeHash()
    {
        IEntityType license = Context.Model.FindEntityType(typeof(License))!;

        Assert.Equal(
            typeof(string),
            license.FindProperty(nameof(License.Kind))!.GetProviderClrType());
        Assert.True(license.FindProperty(nameof(License.CodeHash))!.IsNullable);
    }

    [Fact]
    public void OnlyOneCurrentActivationMayExistPerUser()
    {
        IEntityType license = Context.Model.FindEntityType(typeof(License))!;
        IIndex redemption = Assert.Single(
            license.GetIndexes(),
            index => index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(License.RedeemedByUserId)]));

        Assert.True(redemption.IsUnique);
        Assert.Equal("\"Status\" = 'Redeemed'", redemption.GetFilter());
    }

    [Fact]
    public void ManualActivationHasItsOwnAdditiveMigration()
    {
        Assert.Contains(
            "20260723175945_AddManualLicenseActivation",
            Context.Database.GetMigrations());
    }
}
