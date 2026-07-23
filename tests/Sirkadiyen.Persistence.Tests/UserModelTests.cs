using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Sirkadiyen.Domain.Identity;
using Sirkadiyen.Infrastructure.Persistence;
using Xunit;

namespace Sirkadiyen.Persistence.Tests;

public sealed class UserModelTests
{
    private static readonly IModel Model = new SirkadiyenDbContext(
        new DbContextOptionsBuilder<SirkadiyenDbContext>()
            .UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused")
            .Options).Model;

    [Fact]
    public void GoogleSubjectAndNormalizedEmailAreIndependentlyUnique()
    {
        IEntityType user = Model.FindEntityType(typeof(User))!;

        IIndex subject = Assert.Single(
            user.GetIndexes(),
            index => index.Properties.Select(property => property.Name)
                .SequenceEqual(["GoogleSubject"]));
        IIndex email = Assert.Single(
            user.GetIndexes(),
            index => index.Properties.Select(property => property.Name)
                .SequenceEqual(["NormalizedEmail"]));

        Assert.True(subject.IsUnique);
        Assert.True(email.IsUnique);
    }

    [Fact]
    public void RoleIsStoredByNameAndUserUsesOptimisticConcurrency()
    {
        IEntityType user = Model.FindEntityType(typeof(User))!;

        Assert.Equal(typeof(string), user.FindProperty(nameof(User.Role))!.GetProviderClrType());
        Assert.True(user.FindProperty(nameof(User.RowVersion))!.IsConcurrencyToken);
    }
}
