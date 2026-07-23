using Sirkadiyen.Domain.ScheduleSources;

namespace Sirkadiyen.Application.StudentProfiles;

/// <summary>
/// Validates a submitted student profile against the server-owned supported
/// schema. Both the profile write path and, later, audience matching use it, so a
/// stored selector is always one the sources publish.
/// </summary>
public static class StudentProfileValidator
{
    public static StudentProfileValidationResult Validate(
        SupportedProfileSchema schema,
        SubmittedStudentProfile submitted)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(submitted);

        SupportedProfileProgram? program = schema.FindProgram(
            submitted.ClassYear,
            submitted.ProgramLanguage);
        if (program is null)
        {
            return StudentProfileValidationResult.Failure(
            [
                new StudentProfileValidationError(
                    StudentProfileValidationErrorCode.UnsupportedProgram,
                    Key: null,
                    $"No supported profile exists for class year {submitted.ClassYear} in the "
                    + $"{submitted.ProgramLanguage} program for {schema.AcademicYear}."),
            ]);
        }

        List<StudentProfileValidationError> errors = [];
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
}

/// <summary>A profile as submitted by a client, before it is validated or stored.</summary>
public sealed record SubmittedStudentProfile
{
    public required int ClassYear { get; init; }

    public required ProgramLanguage ProgramLanguage { get; init; }

    public required IReadOnlyDictionary<string, string> Selectors { get; init; }
}

public sealed record StudentProfileValidationResult
{
    private StudentProfileValidationResult(
        bool isValid,
        IReadOnlyList<StudentProfileValidationError> errors)
    {
        IsValid = isValid;
        Errors = errors;
    }

    public bool IsValid { get; }

    public IReadOnlyList<StudentProfileValidationError> Errors { get; }

    public static StudentProfileValidationResult Success() => new(true, []);

    public static StudentProfileValidationResult Failure(
        IReadOnlyList<StudentProfileValidationError> errors) => new(false, errors);
}

public sealed record StudentProfileValidationError(
    StudentProfileValidationErrorCode Code,
    string? Key,
    string Message);

public enum StudentProfileValidationErrorCode
{
    UnsupportedProgram,
    UnknownSelector,
    MissingRequiredSelector,
    MissingDependency,
    UnsupportedValue,
}
