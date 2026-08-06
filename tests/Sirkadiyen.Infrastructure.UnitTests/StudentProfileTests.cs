using Sirkadiyen.Domain.Scheduling.Sources;
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
            " 0101240048 ",
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
        Assert.Equal("0101240048", profile.StudentNumber);
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
            "0101240048",
            "1.0",
            new Dictionary<string, string> { ["practiceGroup"] = "A" },
            Now);
        Guid id = profile.Id;

        profile.Update(
            "2025-2026",
            1,
            ProgramLanguage.English,
            "0102240048",
            "1.0",
            new Dictionary<string, string> { ["practiceGroup"] = "İ" },
            Now.AddHours(1));

        Assert.Equal(id, profile.Id);
        Assert.Equal(UserId, profile.UserId);
        Assert.Equal(ProgramLanguage.English, profile.ProgramLanguage);
        Assert.Equal("0102240048", profile.StudentNumber);
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

    [Theory]
    [InlineData("010124004")]    // nine digits
    [InlineData("01012400489")]  // eleven digits
    [InlineData("010124004X")]   // ten characters, one not a digit
    [InlineData("01 1240048")]   // ten characters, an embedded space
    [InlineData("   ")]          // blank
    public void AStudentNumberThatIsNotExactlyTenDigitsIsRejected(string studentNumber) =>
        Assert.Throws<ArgumentException>(() => Create(studentNumber: studentNumber));

    [Fact]
    public void AStudentNumberIsTrimmedButLeadingZerosAreKept()
    {
        StudentProfile profile = Create(studentNumber: " 0101240048 ");

        Assert.Equal("0101240048", profile.StudentNumber);
    }

    [Fact]
    public void AnIdenticalProfileDescribesTheSameAudience()
    {
        StudentProfile profile = Create();

        Assert.True(profile.DescribesSameAudienceAs(
            "2025-2026",
            1,
            ProgramLanguage.Turkish,
            new Dictionary<string, string> { ["practiceGroup"] = "A" }));
    }

    [Fact]
    public void SelectorsAreComparedAfterTheSameNormalizationTheUpdateApplies()
    {
        StudentProfile profile = Create();

        // Surrounding whitespace is trimmed on the way in, so it must not read as a change and
        // queue a full calendar re-synchronization (ADR-096).
        Assert.True(profile.DescribesSameAudienceAs(
            " 2025-2026 ",
            1,
            ProgramLanguage.Turkish,
            new Dictionary<string, string> { [" practiceGroup "] = " A " }));
    }

    [Theory]
    [InlineData(2, ProgramLanguage.Turkish, "A")]
    [InlineData(1, ProgramLanguage.English, "A")]
    [InlineData(1, ProgramLanguage.Turkish, "B")]
    public void AChangedCohortDimensionIsAnAudienceChange(
        int classYear,
        ProgramLanguage language,
        string practiceGroup)
    {
        StudentProfile profile = Create();

        Assert.False(profile.DescribesSameAudienceAs(
            "2025-2026",
            classYear,
            language,
            new Dictionary<string, string> { ["practiceGroup"] = practiceGroup }));
    }

    [Fact]
    public void AddingOrRemovingASelectorIsAnAudienceChange()
    {
        StudentProfile profile = Create();

        Assert.False(profile.DescribesSameAudienceAs(
            "2025-2026",
            1,
            ProgramLanguage.Turkish,
            new Dictionary<string, string>
            {
                ["practiceGroup"] = "A",
                ["anatomyGroup"] = "2",
            }));

        Assert.False(profile.DescribesSameAudienceAs(
            "2025-2026",
            1,
            ProgramLanguage.Turkish,
            new Dictionary<string, string>()));
    }

    [Fact]
    public void AProfileWhoseOnlyChangeIsTheStudentNumberStillDescribesTheSameAudience()
    {
        // Correcting a typo in the number changes nothing about which lessons apply, so it must
        // not queue calendar work (ADR-096).
        StudentProfile profile = Create(studentNumber: "0101240048");
        Dictionary<string, string> selectors = new() { ["practiceGroup"] = "A" };

        profile.Update(
            "2025-2026",
            1,
            ProgramLanguage.Turkish,
            "0101240049",
            "1.0",
            selectors,
            Now.AddMinutes(1));

        Assert.Equal("0101240049", profile.StudentNumber);
        Assert.True(profile.DescribesSameAudienceAs(
            "2025-2026",
            1,
            ProgramLanguage.Turkish,
            selectors));
    }

    private static StudentProfile Create(
        Guid? userId = null,
        string academicYear = "2025-2026",
        int classYear = 1,
        string studentNumber = "0101240048",
        IReadOnlyDictionary<string, string>? selectors = null) =>
        StudentProfile.Create(
            userId ?? UserId,
            academicYear,
            classYear,
            ProgramLanguage.Turkish,
            studentNumber,
            "1.0",
            selectors ?? new Dictionary<string, string> { ["practiceGroup"] = "A" },
            Now);
}
