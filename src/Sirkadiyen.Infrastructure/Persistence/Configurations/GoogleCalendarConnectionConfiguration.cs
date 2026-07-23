using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sirkadiyen.Domain.GoogleCalendar;
using Sirkadiyen.Domain.Identity;

namespace Sirkadiyen.Infrastructure.Persistence.Configurations;

internal sealed class GoogleCalendarConnectionConfiguration
    : IEntityTypeConfiguration<GoogleCalendarConnection>
{
    public void Configure(EntityTypeBuilder<GoogleCalendarConnection> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("google_calendar_connections");
        builder.HasKey(connection => connection.Id);
        builder.Property(connection => connection.Id).ValueGeneratedNever();

        // Ciphertext, never a readable credential (ADR-057).
        builder.Property(connection => connection.ProtectedRefreshToken)
            .HasMaxLength(GoogleCalendarConnection.MaximumProtectedRefreshTokenLength)
            .IsRequired();
        builder.Property(connection => connection.GrantedScopes)
            .HasMaxLength(GoogleCalendarConnection.MaximumGrantedScopesLength)
            .IsRequired();
        builder.Property(connection => connection.ManagedCalendarId)
            .HasMaxLength(GoogleCalendarConnection.MaximumManagedCalendarIdLength);
        builder.Property(connection => connection.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(connection => connection.RowVersion).IsRowVersion();

        // One connection per account: onboarding and the synchronization path both read
        // "the" connection, so the schema forbids a second.
        builder.HasIndex(connection => connection.UserId).IsUnique();
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(connection => connection.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_google_calendar_connections_status",
            "\"Status\" IN ('Authorized', 'NeedsReauthorization')"));
    }
}
