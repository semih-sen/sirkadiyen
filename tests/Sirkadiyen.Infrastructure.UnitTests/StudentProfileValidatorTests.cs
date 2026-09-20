using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Domain.Scheduling.Sources;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

public sealed class StudentProfileValidatorTests
{
    private static readonly SupportedProfileSchema Schema = CurrentSupportedProfileSchema.Create();

    [Fact]
    public void AConfirmedTurkishCohortIsValid()
    {
        StudentProfileValidationResult result = Validate(
            1,
            ProgramLanguage.Turkish,
            ("practiceGroup", "A"),
            ("practiceSubgroup", "A1"));

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void AConfirmedEnglishCohortIsValid()
    {
        StudentProfileValidationResult result = Validate(
            1,
            ProgramLanguage.English,
            ("practiceGroup", "İ"),
            ("practiceSubgroup", "İ3"));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void AConfirmedGradeTwoTurkishCohortIsValid()
    {
        StudentProfileValidationResult result = Validate(
            2,
            ProgramLanguage.Turkish,
            ("practiceGroup", "C"),
            ("practiceSubgroup", "C2"),
            ("anatomyGroup", "3"));

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void AGradeTwoProfileWithoutItsAnatomyGroupIsRejected()
    {
        // The dissection rotation is the whole reason the anatomy documents are
        // parsed; a Grade 2 profile that omits it would silently receive none of it.
        StudentProfileValidationResult result = Validate(
            2,
            ProgramLanguage.Turkish,
            ("practiceGroup", "C"),
            ("practiceSubgroup", "C2"));

        StudentProfileValidationError error = Assert.Single(result.Errors);
        Assert.Equal(StudentProfileValidationErrorCode.MissingRequiredSelector, error.Code);
        Assert.Equal("anatomyGroup", error.Key);
    }

    [Fact]
    public void AnAnatomyGroupOutsideTheThreeTheSourceStatesIsRejected()
    {
        StudentProfileValidationResult result = Validate(
            2,
            ProgramLanguage.Turkish,
            ("practiceGroup", "C"),
            ("practiceSubgroup", "C2"),
            ("anatomyGroup", "4"));

        StudentProfileValidationError error = Assert.Single(result.Errors);
        Assert.Equal(StudentProfileValidationErrorCode.UnsupportedValue, error.Code);
        Assert.Equal("anatomyGroup", error.Key);
    }

    [Fact]
    public void GradeTwoEnglishHasNoConfirmedProfileYet()
    {
        StudentProfileValidationResult result = ValidateWith(
            2,
            ProgramLanguage.English,
            "0102240048");

        StudentProfileValidationError error = Assert.Single(result.Errors);
        Assert.Equal(StudentProfileValidationErrorCode.UnsupportedProgram, error.Code);
    }

    [Fact]
    public void AClassYearWithNoConfirmedProfileIsUnsupported()
    {
        // Grade 4 has no captured source at all, so it is the honest example of a
        // class year nothing publishes for.
        StudentProfileValidationResult result = Validate(4, ProgramLanguage.Turkish);

        StudentProfileValidationError error = Assert.Single(result.Errors);
        Assert.Equal(StudentProfileValidationErrorCode.UnsupportedProgram, error.Code);
    }

    [Fact]
    public void AConfirmedGradeThreeTurkishCohortIsValid()
    {
        StudentProfileValidationResult result = Validate(
            3,
            ProgramLanguage.Turkish,
            ("curriculumGroup", "3-A"),
            ("facultyPracticeGroup", "A5"),
            ("microPathologyGroup", "A1"));

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    /// <summary>
    /// A cohort belongs to one curriculum group's rotation, so the pair has to be
    /// consistent (ADR-099).
    /// </summary>
    /// <remarks>
    /// The A and B faculty-practice programs are separate documents with separate
    /// rotations. A student in <c>3-A</c> who declared <c>B5</c> would match no
    /// published session at all, and nothing downstream would report why.
    /// </remarks>
    [Fact]
    public void AGradeThreeCohortFromTheOtherCurriculumGroupIsRejected()
    {
        StudentProfileValidationResult result = Validate(
            3,
            ProgramLanguage.Turkish,
            ("curriculumGroup", "3-A"),
            ("facultyPracticeGroup", "B5"),
            ("microPathologyGroup", "A1"));

        StudentProfileValidationError error = Assert.Single(result.Errors);
        Assert.Equal(StudentProfileValidationErrorCode.UnsupportedValue, error.Code);
        Assert.Equal("facultyPracticeGroup", error.Key);
    }

    /// <summary>
    /// Grade 3 English declares its faculty-practice cohort (A1-A4, independent) and its
    /// microbiology/pathology group, and — unlike Turkish — no curriculum group
    /// (ADR-160; ADR-098 stands on the A/B point).
    /// </summary>
    [Fact]
    public void AConfirmedGradeThreeEnglishCohortIsValid()
    {
        StudentProfileValidationResult result = ValidateWith(
            3,
            ProgramLanguage.English,
            "0102240048",
            ("facultyPracticeGroup", "A2"),
            ("microPathologyGroup", "B1"));

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    /// <summary>
    /// A curriculum group is not a dimension Grade 3 English declares (ADR-098), so a
    /// profile that carries one is reported rather than quietly accepted (ADR-160).
    /// </summary>
    [Fact]
    public void AGradeThreeEnglishProfileWithACurriculumGroupIsRejected()
    {
        StudentProfileValidationResult result = ValidateWith(
            3,
            ProgramLanguage.English,
            "0102240048",
            ("curriculumGroup", "3-A"),
            ("facultyPracticeGroup", "A2"),
            ("microPathologyGroup", "B1"));

        StudentProfileValidationError error = Assert.Single(result.Errors);
        Assert.Equal(StudentProfileValidationErrorCode.UnknownSelector, error.Code);
        Assert.Equal("curriculumGroup", error.Key);
    }

    /// <summary>
    /// The English faculty-practice cohort is capped at A1-A4 — the cohorts the faculty
    /// placed the English students in — so a value the A document holds but no English
    /// student is in (A5-A8) is rejected (ADR-160).
    /// </summary>
    [Fact]
    public void AGradeThreeEnglishFacultyGroupOutsideItsFourCohortsIsRejected()
    {
        StudentProfileValidationResult result = ValidateWith(
            3,
            ProgramLanguage.English,
            "0102240048",
            ("facultyPracticeGroup", "A5"),
            ("microPathologyGroup", "B1"));

        StudentProfileValidationError error = Assert.Single(result.Errors);
        Assert.Equal(StudentProfileValidationErrorCode.UnsupportedValue, error.Code);
        Assert.Equal("facultyPracticeGroup", error.Key);
    }

    /// <summary>
    /// Every Grade 3 English dimension is required, so a profile omitting one is
    /// reported rather than accepted as program-wide (ADR-160).
    /// </summary>
    [Fact]
    public void AGradeThreeEnglishProfileWithoutItsGroupIsRejected()
    {
        StudentProfileValidationResult result = ValidateWith(
            3,
            ProgramLanguage.English,
            "0102240048",
            ("facultyPracticeGroup", "A2"));

        StudentProfileValidationError error = Assert.Single(result.Errors);
        Assert.Equal(StudentProfileValidationErrorCode.MissingRequiredSelector, error.Code);
        Assert.Equal("microPathologyGroup", error.Key);
    }

    [Fact]
    public void AMissingRequiredSelectorIsReported()
    {
        StudentProfileValidationResult result = Validate(
            1,
            ProgramLanguage.Turkish,
            ("practiceGroup", "A"));

        StudentProfileValidationError error = Assert.Single(result.Errors);
        Assert.Equal(StudentProfileValidationErrorCode.MissingRequiredSelector, error.Code);
        Assert.Equal("practiceSubgroup", error.Key);
    }

    [Fact]
    public void AnUnknownSelectorKeyIsReported()
    {
        StudentProfileValidationResult result = Validate(
            1,
            ProgramLanguage.Turkish,
            ("practiceGroup", "A"),
            ("practiceSubgroup", "A1"),
            ("anatomyGroup", "2"));

        StudentProfileValidationError error = Assert.Single(result.Errors);
        Assert.Equal(StudentProfileValidationErrorCode.UnknownSelector, error.Code);
        Assert.Equal("anatomyGroup", error.Key);
    }

    [Fact]
    public void AnUnsupportedIndependentValueIsReported()
    {
        StudentProfileValidationResult result = Validate(
            1,
            ProgramLanguage.Turkish,
            ("practiceGroup", "Z"),
            ("practiceSubgroup", "A1"));

        Assert.Contains(
            result.Errors,
            error => error is
            {
                Code: StudentProfileValidationErrorCode.UnsupportedValue,
                Key: "practiceGroup",
            });
    }

    [Fact]
    public void ASubgroupThatDoesNotBelongToTheChosenGroupIsRejected()
    {
        StudentProfileValidationResult result = Validate(
            1,
            ProgramLanguage.Turkish,
            ("practiceGroup", "A"),
            ("practiceSubgroup", "B1"));

        StudentProfileValidationError error = Assert.Single(result.Errors);
        Assert.Equal(StudentProfileValidationErrorCode.UnsupportedValue, error.Code);
        Assert.Equal("practiceSubgroup", error.Key);
    }

    [Fact]
    public void ADependentSelectorWithoutItsParentIsReported()
    {
        StudentProfileValidationResult result = Validate(
            1,
            ProgramLanguage.Turkish,
            ("practiceSubgroup", "A1"));

        Assert.Contains(
            result.Errors,
            error => error is
            {
                Code: StudentProfileValidationErrorCode.MissingRequiredSelector,
                Key: "practiceGroup",
            });
        Assert.Contains(
            result.Errors,
            error => error is
            {
                Code: StudentProfileValidationErrorCode.MissingDependency,
                Key: "practiceSubgroup",
            });
    }

    [Fact]
    public void ANonNumericStudentNumberIsRejected()
    {
        StudentProfileValidationResult result = ValidateWith(
            1,
            ProgramLanguage.Turkish,
            "01012400XY",
            ("practiceGroup", "A"),
            ("practiceSubgroup", "A1"));

        Assert.Contains(
            result.Errors,
            error => error.Code == StudentProfileValidationErrorCode.InvalidStudentNumber);
    }

    [Theory]
    [InlineData("010124004")]    // nine digits
    [InlineData("01012400489")]  // eleven digits
    [InlineData("")]             // missing
    public void AStudentNumberThatIsNotTenDigitsIsRejected(string studentNumber)
    {
        StudentProfileValidationResult result = ValidateWith(
            1,
            ProgramLanguage.Turkish,
            studentNumber,
            ("practiceGroup", "A"),
            ("practiceSubgroup", "A1"));

        StudentProfileValidationError error = Assert.Single(
            result.Errors,
            candidate => candidate.Key == "studentNumber");
        Assert.Equal(StudentProfileValidationErrorCode.InvalidStudentNumber, error.Code);
    }

    [Fact]
    public void AStudentNumberFromAnotherFacultyIsRejected()
    {
        StudentProfileValidationResult result = ValidateWith(
            1,
            ProgramLanguage.Turkish,
            "0201240048",
            ("practiceGroup", "A"),
            ("practiceSubgroup", "A1"));

        StudentProfileValidationError error = Assert.Single(
            result.Errors,
            candidate => candidate.Key == "studentNumber");
        Assert.Equal(
            StudentProfileValidationErrorCode.StudentNumberFacultyMismatch,
            error.Code);
    }

    [Fact]
    public void AnEnglishStudentNumberUnderTheTurkishProgramIsRejected()
    {
        StudentProfileValidationResult result = ValidateWith(
            1,
            ProgramLanguage.Turkish,
            "0102240048",
            ("practiceGroup", "A"),
            ("practiceSubgroup", "A1"));

        StudentProfileValidationError error = Assert.Single(
            result.Errors,
            candidate => candidate.Key == "studentNumber");
        Assert.Equal(
            StudentProfileValidationErrorCode.StudentNumberProgramMismatch,
            error.Code);
    }

    [Fact]
    public void ATurkishStudentNumberUnderTheEnglishProgramIsRejected()
    {
        StudentProfileValidationResult result = ValidateWith(
            1,
            ProgramLanguage.English,
            "0101240048",
            ("practiceGroup", "İ"),
            ("practiceSubgroup", "İ1"));

        StudentProfileValidationError error = Assert.Single(
            result.Errors,
            candidate => candidate.Key == "studentNumber");
        Assert.Equal(
            StudentProfileValidationErrorCode.StudentNumberProgramMismatch,
            error.Code);
    }

    [Fact]
    public void AMatchingEnglishStudentNumberIsAccepted()
    {
        StudentProfileValidationResult result = ValidateWith(
            1,
            ProgramLanguage.English,
            "0102240048",
            ("practiceGroup", "İ"),
            ("practiceSubgroup", "İ2"));

        Assert.True(result.IsValid);
    }

    private static StudentProfileValidationResult Validate(
        int classYear,
        ProgramLanguage programLanguage,
        params (string Key, string Value)[] selectors) =>
        ValidateWith(classYear, programLanguage, DefaultStudentNumber(programLanguage), selectors);

    private static StudentProfileValidationResult ValidateWith(
        int classYear,
        ProgramLanguage programLanguage,
        string studentNumber,
        params (string Key, string Value)[] selectors) =>
        StudentProfileValidator.Validate(
            Schema,
            new SubmittedStudentProfile
            {
                ClassYear = classYear,
                ProgramLanguage = programLanguage,
                StudentNumber = studentNumber,
                Selectors = selectors.ToDictionary(
                    selector => selector.Key,
                    selector => selector.Value,
                    StringComparer.Ordinal),
            });

    [Fact]
    public void SelectorsAloneValidateWithoutAStudentNumber()
    {
        // The cohort schedule simulation states a cohort but has no student, so it must be able
        // to reach the selector rules without a number the validator would reject it for.
        StudentProfileValidationResult result = StudentProfileValidator.ValidateSelectors(
            Schema,
            1,
            ProgramLanguage.Turkish,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["practiceGroup"] = "A",
                ["practiceSubgroup"] = "A1",
            });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void SelectorsAloneStillRefuseAnUnknownKey()
    {
        StudentProfileValidationResult result = StudentProfileValidator.ValidateSelectors(
            Schema,
            1,
            ProgramLanguage.Turkish,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["practiceGroup"] = "A",
                ["practiceSubgroup"] = "A1",
                ["anatomyGroup"] = "1",
            });

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.Code == StudentProfileValidationErrorCode.UnknownSelector
                && error.Key == "anatomyGroup");
    }

    [Fact]
    public void SelectorsAloneStillRequireEveryRequiredDimension()
    {
        StudentProfileValidationResult result = StudentProfileValidator.ValidateSelectors(
            Schema,
            1,
            ProgramLanguage.Turkish,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["practiceGroup"] = "A",
            });

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.Code == StudentProfileValidationErrorCode.MissingRequiredSelector
                && error.Key == "practiceSubgroup");
    }

    [Fact]
    public void SelectorsAloneStillRefuseAnUnsupportedProgram()
    {
        StudentProfileValidationResult result = StudentProfileValidator.ValidateSelectors(
            Schema,
            6,
            ProgramLanguage.Turkish,
            new Dictionary<string, string>(StringComparer.Ordinal));

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.Code == StudentProfileValidationErrorCode.UnsupportedProgram);
    }

    /// <summary>A well-formed student number whose program code matches the language.</summary>
    private static string DefaultStudentNumber(ProgramLanguage programLanguage) =>
        programLanguage == ProgramLanguage.English ? "0102240048" : "0101240048";
}
