using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Sirkadiyen.Contracts.Serialization;
using Sirkadiyen.Domain.Identity;
using Sirkadiyen.Domain.StudentProfiles;

namespace Sirkadiyen.Infrastructure.Persistence.Configurations;

/// <summary>Stores the cohort selectors as a JSONB key/value document.</summary>
/// <remarks>
/// The variable cohort dimensions differ by class year and evolve without a schema
/// migration, so a fixed set of columns would fight the model. A JSONB document
/// keeps them flexible while the relational academic-year/class-year/language
/// columns carry every audience query (systemPatterns §22).
/// </remarks>
internal sealed class ProfileSelectorsConverter()
    : ValueConverter<IReadOnlyDictionary<string, string>, string>(
        selectors => JsonSerializer.Serialize(selectors, SerializerOptions),
        json => Deserialize(json))
{
    private static readonly JsonSerializerOptions SerializerOptions = ContractJson.CreateOptions();

    private static IReadOnlyDictionary<string, string> Deserialize(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json, SerializerOptions)
        ?? new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>Compares selector documents by key/value so change tracking is exact.</summary>
/// <remarks>
/// Without a value comparer, EF compares dictionary references and would either
/// miss an edit or rewrite the column on every save.
/// </remarks>
internal sealed class ProfileSelectorsComparer()
    : ValueComparer<IReadOnlyDictionary<string, string>>(
        (left, right) => Equal(left, right),
        selectors => HashOf(selectors),
        selectors => CopyOf(selectors))
{
    private static bool Equal(
        IReadOnlyDictionary<string, string>? left,
        IReadOnlyDictionary<string, string>? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        foreach ((string key, string value) in left)
        {
            if (!right.TryGetValue(key, out string? other)
                || !string.Equals(value, other, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static int HashOf(IReadOnlyDictionary<string, string>? selectors)
    {
        if (selectors is null)
        {
            return 0;
        }

        // Order-independent so two equal documents hash the same regardless of
        // enumeration order.
        int hash = 0;
        foreach ((string key, string value) in selectors)
        {
            hash ^= HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(key),
                StringComparer.Ordinal.GetHashCode(value));
        }

        return hash;
    }

    private static IReadOnlyDictionary<string, string> CopyOf(
        IReadOnlyDictionary<string, string>? selectors) =>
        selectors is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(selectors, StringComparer.Ordinal);
}

internal sealed class StudentProfileConfiguration : IEntityTypeConfiguration<StudentProfile>
{
    public void Configure(EntityTypeBuilder<StudentProfile> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("student_profiles");
        builder.HasKey(profile => profile.Id);
        builder.Property(profile => profile.Id).ValueGeneratedNever();

        builder.Property(profile => profile.AcademicYear)
            .HasMaxLength(StudentProfile.MaximumAcademicYearLength)
            .IsRequired();
        builder.Property(profile => profile.ProgramLanguage)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(profile => profile.SelectorSchemaVersion)
            .HasMaxLength(StudentProfile.MaximumSchemaVersionLength)
            .IsRequired();
        builder.Property(profile => profile.Selectors)
            .HasConversion(new ProfileSelectorsConverter())
            .HasColumnType("jsonb")
            .IsRequired()
            .Metadata.SetValueComparer(new ProfileSelectorsComparer());
        builder.Property(profile => profile.RowVersion).IsRowVersion();

        // One profile per account. The audience resolver and onboarding both read
        // "the" profile, so the schema forbids a second.
        builder.HasIndex(profile => profile.UserId).IsUnique();
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(profile => profile.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_student_profiles_program_language",
            "\"ProgramLanguage\" IN ('Turkish', 'English')"));
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_student_profiles_class_year",
            $"\"ClassYear\" BETWEEN {StudentProfile.MinimumClassYear} "
            + $"AND {StudentProfile.MaximumClassYear}"));
    }
}
