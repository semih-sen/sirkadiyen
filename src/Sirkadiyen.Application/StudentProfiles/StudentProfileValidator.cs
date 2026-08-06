using Sirkadiyen.Domain.ScheduleSources;
using Sirkadiyen.Domain.StudentProfiles;

namespace Sirkadiyen.Application.StudentProfiles;

/// <summary>
/// Validates a submitted student profile against the server-owned supported
/// schema. Both the profile write path and, later, audience matching use it, so a
/// stored selector is always one the sources publish.
/// </summary>
public static class StudentProfileValidator
{
    /// <summary>The field name reported for every student-number error.</summary>
    private const string StudentNumberKey = "studentNumber";

    /// <summary>Digits 1-2 of every in-scope student number: Istanbul Medical Faculty.</summary>
    private const string IstanbulMedicalFacultyCode = "01";

    /// <summary>Digits 3-4 encode the program language.</summary>
    private const string TurkishProgramCode = "01";

    private const string EnglishProgramCode = "02";

    public static StudentProfileValidationResult Validate(
        SupportedProfileSchema schema,
        SubmittedStudentProfile submitted)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(submitted);

        List<StudentProfileValidationError> errors = [];

        // The student number is validated independently of the program lookup so its
        // errors surface even when the class year has no supported profile.
        ValidateStudentNumber(submitted, errors);

        SupportedProfileProgram? program = schema.FindProgram(
            submitted.ClassYear,
            submitted.ProgramLanguage);
        if (program is null)
        {
            errors.Add(new StudentProfileValidationError(
                StudentProfileValidationErrorCode.UnsupportedProgram,
                Key: null,
                $"No supported profile exists for class year {submitted.ClassYear} in the "
                + $"{submitted.ProgramLanguage} program for {schema.AcademicYear}."));
            return StudentProfileValidationResult.Failure(errors);
        }

        IReadOnlyDictionary<string, string> selectors = submitted.Selectors;

        // A key the program does not define cannot be validated and must not be
        // silently stored, so it is reported rather than dropped.
        foreach (string key in selectors.Keys)
        {
            if (program.FindDimension(key) is null)
            {
                errors.Add(new StudentProfileValidationError(
                    StudentProfileValidationErrorCode.UnknownSelector,
                    key,
                    $"'{key}' is not a selector for this program."));
            }
        }

        foreach (SupportedProfileDimension dimension in program.Dimensions)
        {
            ValidateDimension(program, dimension, selectors, errors);
        }

        return errors.Count == 0
            ? StudentProfileValidationResult.Success()
            : StudentProfileValidationResult.Failure(errors);
    }

    private static void ValidateDimension(
        SupportedProfileProgram program,
        SupportedProfileDimension dimension,
        IReadOnlyDictionary<string, string> selectors,
        List<StudentProfileValidationError> errors)
    {
        bool present = selectors.TryGetValue(dimension.Key, out string? value);

        if (!present)
        {
            if (dimension.Required)
            {
                errors.Add(new StudentProfileValidationError(
                    StudentProfileValidationErrorCode.MissingRequiredSelector,
                    dimension.Key,
                    $"'{dimension.Key}' is required."));
            }

            return;
        }

        if (!dimension.IsDependent)
        {
            if (!dimension.AllowedValuesFor(null).Contains(value, StringComparer.Ordinal))
            {
                errors.Add(UnsupportedValue(dimension.Key, value));
            }

            return;
        }

        // A dependent dimension can only be judged once its parent is known and
        // valid. Report the dependency failure rather than a misleading
        // "unsupported value" so the client fixes the actual cause.
        string parentKey = dimension.DependsOn!;
        if (!selectors.TryGetValue(parentKey, out string? parentValue))
        {
            errors.Add(new StudentProfileValidationError(
                StudentProfileValidationErrorCode.MissingDependency,
                dimension.Key,
                $"'{dimension.Key}' requires '{parentKey}' to be set."));
            return;
        }

        SupportedProfileDimension? parent = program.FindDimension(parentKey);
        bool parentIsValid = parent is not null
            && parent.AllowedValuesFor(null).Contains(parentValue, StringComparer.Ordinal);
        if (!parentIsValid)
        {
            // The parent selector is itself invalid; its own error is already
            // recorded, so do not also blame the child for an unresolvable parent.
            return;
        }

        if (!dimension.AllowedValuesFor(parentValue).Contains(value, StringComparer.Ordinal))
        {
            errors.Add(new StudentProfileValidationError(
                StudentProfileValidationErrorCode.UnsupportedValue,
                dimension.Key,
                $"'{value}' is not a valid '{dimension.Key}' for '{parentKey}' '{parentValue}'."));
        }
    }

    private static StudentProfileValidationError UnsupportedValue(string key, string? value) =>
        new(
            StudentProfileValidationErrorCode.UnsupportedValue,
            key,
            $"'{value}' is not a supported '{key}'.");

    /// <summary>
    /// Enforces the student number's format and its cross-validation against the
    /// selected program: the first two digits must be the Istanbul Medical Faculty
    /// code and the next two must match the program language.
    /// </summary>
    private static void ValidateStudentNumber(
        SubmittedStudentProfile submitted,
        List<StudentProfileValidationError> errors)
    {
        string number = submitted.StudentNumber?.Trim() ?? string.Empty;

        if (number.Length != StudentProfile.StudentNumberLength
            || !number.All(char.IsAsciiDigit))
        {
            errors.Add(new StudentProfileValidationError(
                StudentProfileValidationErrorCode.InvalidStudentNumber,
                StudentNumberKey,
                $"A student number must be exactly {StudentProfile.StudentNumberLength} digits."));

            // The digit-slice rules below are meaningless on a malformed number.
            return;
        }

        string facultyCode = number[..2];
        if (!string.Equals(facultyCode, IstanbulMedicalFacultyCode, StringComparison.Ordinal))
        {
            errors.Add(new StudentProfileValidationError(
                StudentProfileValidationErrorCode.StudentNumberFacultyMismatch,
                StudentNumberKey,
                $"A student number must begin with the Istanbul Medical Faculty code "
                + $"'{IstanbulMedicalFacultyCode}'."));
        }

        string languageCode = number[2..4];
        string expectedLanguageCode = ProgramLanguageCode(submitted.ProgramLanguage);
        if (!string.Equals(languageCode, expectedLanguageCode, StringComparison.Ordinal))
        {
            errors.Add(new StudentProfileValidationError(
                StudentProfileValidationErrorCode.StudentNumberProgramMismatch,
                StudentNumberKey,
                $"The student number's program code '{languageCode}' does not match the selected "
                + $"{submitted.ProgramLanguage} program (expected '{expectedLanguageCode}')."));
        }
    }

    private static string ProgramLanguageCode(ProgramLanguage language) => language switch
    {
        ProgramLanguage.Turkish => TurkishProgramCode,
        ProgramLanguage.English => EnglishProgramCode,
        _ => throw new ArgumentOutOfRangeException(
            nameof(language),
            language,
            "Unknown program language."),
    };
}
