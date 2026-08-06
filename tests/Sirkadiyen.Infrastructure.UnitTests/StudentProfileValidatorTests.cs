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
        StudentProfileValidationResult result = Validate(3, ProgramLanguage.Turkish);

        StudentProfileValidationError error = Assert.Single(result.Errors);
        Assert.Equal(StudentProfileValidationErrorCode.UnsupportedProgram, error.Code);
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

    /// <summary>A well-formed student number whose program code matches the language.</summary>
    private static string DefaultStudentNumber(ProgramLanguage programLanguage) =>
        programLanguage == ProgramLanguage.English ? "0102240048" : "0101240048";
}
