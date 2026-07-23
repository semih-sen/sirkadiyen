using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Domain.ScheduleSources;
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

    private static StudentProfileValidationResult Validate(
        int classYear,
        ProgramLanguage programLanguage,
        params (string Key, string Value)[] selectors) =>
        StudentProfileValidator.Validate(
            Schema,
            new SubmittedStudentProfile
            {
                ClassYear = classYear,
                ProgramLanguage = programLanguage,
                Selectors = selectors.ToDictionary(
                    selector => selector.Key,
                    selector => selector.Value,
                    StringComparer.Ordinal),
            });
}
