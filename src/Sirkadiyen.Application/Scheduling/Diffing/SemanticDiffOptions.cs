namespace Sirkadiyen.Application.ScheduleDiffing;

/// <summary>
/// Deterministic thresholds for recognizing a lesson whose time changed.
/// </summary>
public sealed record SemanticDiffOptions
{
    public decimal MinimumTitleSimilarity { get; init; } = 0.82m;

    public decimal MinimumInstructorSimilarity { get; init; } = 0.85m;

    public decimal MinimumDepartmentSimilarity { get; init; } = 0.90m;

    public decimal MinimumCompositeSimilarity { get; init; } = 0.88m;

    /// <summary>
    /// The composite bar for a match made without a comparable department.
    /// </summary>
    /// <remarks>
    /// More than half of the lessons the confirmed sources publish state no
    /// department, and an integrated session states several, which is not a
    /// comparable value. Refusing to match those would send every time change
    /// they contain to the calendar as a delete followed by a create. Matching
    /// them on title and instructor alone is weaker evidence, so it has to clear
    /// a higher bar than a three-attribute match (ADR-035 as amended).
    /// </remarks>
    public decimal MinimumCompositeSimilarityWithoutDepartment { get; init; } = 0.94m;

    public decimal TitleWeight { get; init; } = 0.50m;

    public decimal InstructorWeight { get; init; } = 0.30m;

    public decimal DepartmentWeight { get; init; } = 0.20m;

    public void Validate()
    {
        ValidateUnitInterval(MinimumTitleSimilarity, nameof(MinimumTitleSimilarity));
        ValidateUnitInterval(MinimumInstructorSimilarity, nameof(MinimumInstructorSimilarity));
        ValidateUnitInterval(MinimumDepartmentSimilarity, nameof(MinimumDepartmentSimilarity));
        ValidateUnitInterval(MinimumCompositeSimilarity, nameof(MinimumCompositeSimilarity));
        ValidateUnitInterval(
            MinimumCompositeSimilarityWithoutDepartment,
            nameof(MinimumCompositeSimilarityWithoutDepartment));
        ValidateUnitInterval(TitleWeight, nameof(TitleWeight));
        ValidateUnitInterval(InstructorWeight, nameof(InstructorWeight));
        ValidateUnitInterval(DepartmentWeight, nameof(DepartmentWeight));

        if (TitleWeight + InstructorWeight + DepartmentWeight != 1.0m)
        {
            throw new ArgumentException("Semantic diff weights must add up to exactly 1.0.");
        }

        if (TitleWeight + InstructorWeight <= 0m)
        {
            throw new ArgumentException(
                "Matching without a department needs a non-zero title or instructor weight.");
        }

        // Weaker evidence must never be easier to satisfy than stronger evidence,
        // whatever an operator configures.
        if (MinimumCompositeSimilarityWithoutDepartment < MinimumCompositeSimilarity)
        {
            throw new ArgumentException(
                "The composite threshold without a department must be at least as high as the "
                + "threshold that includes one.");
        }
    }

    private static void ValidateUnitInterval(decimal value, string parameterName)
    {
        if (value is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Semantic diff thresholds and weights must be between 0 and 1.");
        }
    }
}
