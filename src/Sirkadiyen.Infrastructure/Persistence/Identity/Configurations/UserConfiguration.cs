using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sirkadiyen.Domain.Identity;

namespace Sirkadiyen.Infrastructure.Persistence.Identity.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("users");
        builder.HasKey(user => user.Id);
        builder.Property(user => user.Id).ValueGeneratedNever();
        builder.Property(user => user.GoogleSubject)
            .HasMaxLength(User.MaximumGoogleSubjectLength)
            .IsRequired();
        builder.Property(user => user.Email)
            .HasMaxLength(User.MaximumEmailLength)
            .IsRequired();
        builder.Property(user => user.NormalizedEmail)
            .HasMaxLength(User.MaximumEmailLength)
            .IsRequired();
        builder.Property(user => user.DisplayName)
            .HasMaxLength(User.MaximumDisplayNameLength);
        builder.Property(user => user.Role)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();
        builder.Property(user => user.RowVersion).IsRowVersion();

        builder.HasIndex(user => user.GoogleSubject).IsUnique();
        builder.HasIndex(user => user.NormalizedEmail).IsUnique();
        builder.HasIndex(user => user.LastSignedInAtUtc);

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_users_role",
            "\"Role\" IN ('User', 'SuperAdmin')"));
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_users_verified_email",
            "\"IsEmailVerified\" = TRUE"));
    }
}
