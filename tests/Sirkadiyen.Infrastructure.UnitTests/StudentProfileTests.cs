using Sirkadiyen.Domain.ScheduleSources;
using Sirkadiyen.Domain.StudentProfiles;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

public sealed class StudentProfileTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid UserId = Guid.CreateVersion7();

    [Fact]
    public void CreateTrimsAndKeepsTheValidatedAcademicFields()
    {
        StudentProfile profile = StudentProfile.Create(
            UserId,
            " 2025-2026 ",
            1,
            ProgramLanguage.Turkish,
            " 1.0 ",
            new Dictionary<string, string>
            {
                [" practiceGroup "] = " A ",
                ["practiceSubgroup"] = "A1",
            },
            Now);

        Assert.NotEqual(Guid.Empty, profile.Id);
        Assert.Equal(UserId, profile.UserId);
        Assert.Equal("2025-2026", profile.AcademicYear);
        Assert.Equal(1, profile.ClassYear);
        Assert.Equal(ProgramLanguage.Turkish, profile.ProgramLanguage);
        Assert.Equal("1.0", profile.SelectorSchemaVersion);
        Assert.Equal("A", profile.Selectors["practiceGroup"]);
        Assert.Equal("A1", profile.Selectors["practiceSubgroup"]);
        Assert.Equal(Now, profile.CreatedAtUtc);
        Assert.Equal(Now, profile.UpdatedAtUtc);
    }

    [Fact]
    public void UpdateReplacesSelectorsAndAdvancesTheTimestampButKeepsIdentity()
    {
        StudentProfile profile = StudentProfile.Create(
            UserId,
            "2025-2026",
            1,
            ProgramLanguage.Turkish,
            "1.0",
            new Dictionary<string, string> { ["practiceGroup"] = "A" },
            Now);
        Guid id = profile.Id;

        profile.Update(
            "2025-2026",
            1,
            ProgramLanguage.English,
            "1.0",
            new Dictionary<string, string> { ["practiceGroup"] = "İ" },
            Now.AddHours(1));

        Assert.Equal(id, profile.Id);
        Assert.Equal(UserId, profile.UserId);
        Assert.Equal(ProgramLanguage.English, profile.ProgramLanguage);
        Assert.Equal("İ", profile.Selectors["practiceGroup"]);
        Assert.Single(profile.Selectors);
        Assert.Equal(Now, profile.CreatedAtUtc);
        Assert.Equal(Now.AddHours(1), profile.UpdatedAtUtc);
    }

    [Fact]
    public void AProfileMustHaveAnOwner() =>
        Assert.Throws<ArgumentException>(() => Create(userId: Guid.Empty));

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void ClassYearOutsideTheSupportedRangeIsRejected(int classYear) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(classYear: classYear));

    [Fact]
    public void ABlankAcademicYearIsRejected() =>
        Assert.Throws<ArgumentException>(() => Create(academicYear: "   "));

    [Fact]
    public void ABlankSelectorKeyOrValueIsRejected()
    {
        Assert.Throws<ArgumentException>(() => Create(
            selectors: new Dictionary<string, string> { ["  "] = "A" }));
        Assert.Throws<ArgumentException>(() => Create(
            selectors: new Dictionary<string, string> { ["practiceGroup"] = "  " }));
    }

    [Fact]
    public void MoreSelectorsThanTheCapAreRejected()
    {
        Dictionary<string, string> selectors = new();
        for (int index = 0; index <= StudentProfile.MaximumSelectorCount; index++)
        {
            selectors[$"dimension{index}"] = "value";
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => Create(selectors: selectors));
    }

    private static StudentProfile Create(
        Guid? userId = null,
        string academicYear = "2025-2026",
        int classYear = 1,
        IReadOnlyDictionary<string, string>? selectors = null) =>
        StudentProfile.Create(
            userId ?? UserId,
            academicYear,
            classYear,
            ProgramLanguage.Turkish,
            "1.0",
            selectors ?? new Dictionary<string, string> { ["practiceGroup"] = "A" },
            Now);
}
