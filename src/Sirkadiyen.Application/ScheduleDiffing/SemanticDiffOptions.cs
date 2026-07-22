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

    public decimal TitleWeight { get; init; } = 0.50m;

    public decimal InstructorWeight { get; init; } = 0.30m;

    public decimal DepartmentWeight { get; init; } = 0.20m;

    public void Validate()
    {
        ValidateUnitInterval(MinimumTitleSimilarity, nameof(MinimumTitleSimilarity));
        ValidateUnitInterval(MinimumInstructorSimilarity, nameof(MinimumInstructorSimilarity));
        ValidateUnitInterval(MinimumDepartmentSimilarity, nameof(MinimumDepartmentSimilarity));
        ValidateUnitInterval(MinimumCompositeSimilarity, nameof(MinimumCompositeSimilarity));
        ValidateUnitInterval(TitleWeight, nameof(TitleWeight));
        ValidateUnitInterval(InstructorWeight, nameof(InstructorWeight));
        ValidateUnitInterval(DepartmentWeight, nameof(DepartmentWeight));

        if (TitleWeight + InstructorWeight + DepartmentWeight != 1.0m)
        {
            throw new ArgumentException("Semantic diff weights must add up to exactly 1.0.");
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
