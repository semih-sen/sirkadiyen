using Sirkadiyen.Domain.ScheduleSources;

namespace Sirkadiyen.Application.StudentProfiles;

/// <summary>A profile as submitted by a client, before it is validated or stored.</summary>
public sealed record SubmittedStudentProfile
{
    public required int ClassYear { get; init; }

    public required ProgramLanguage ProgramLanguage { get; init; }

    /// <summary>The raw student number as submitted; validated, never trusted.</summary>
    public required string StudentNumber { get; init; }

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

    /// <summary>The student number is not exactly ten digits.</summary>
    InvalidStudentNumber,

    /// <summary>The student number's faculty code is not Istanbul Medical Faculty.</summary>
    StudentNumberFacultyMismatch,

    /// <summary>The student number's program-language code contradicts the selected program.</summary>
    StudentNumberProgramMismatch,
}
