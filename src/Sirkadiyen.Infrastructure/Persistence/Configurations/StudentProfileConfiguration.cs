using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sirkadiyen.Domain.Identity;
using Sirkadiyen.Domain.StudentProfiles;

namespace Sirkadiyen.Infrastructure.Persistence.Configurations;

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
        builder.Property(profile => profile.StudentNumber)
            .HasMaxLength(StudentProfile.StudentNumberLength)
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

        // Defence in depth: the exact-ten-digit structural invariant the domain and
        // the application validator both enforce is also pinned at the database. The
        // semantic faculty/language digit rules stay in the application layer because
        // they depend on the row's own program language.
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_student_profiles_student_number",
            "\"StudentNumber\" ~ '^[0-9]{10}$'"));
    }
}
