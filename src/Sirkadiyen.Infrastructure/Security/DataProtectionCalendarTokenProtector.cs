using Microsoft.AspNetCore.DataProtection;
using Sirkadiyen.Application.GoogleCalendar;

namespace Sirkadiyen.Infrastructure.Security;

/// <summary>
/// Encrypts the Google refresh token at rest using ASP.NET Core Data Protection.
/// </summary>
/// <remarks>
/// The purpose string binds the ciphertext to this use: a payload protected for the
/// session cookie cannot be unprotected here, and vice versa. It is part of the
/// cryptographic contract, so changing it invalidates every stored token.
/// <para>
/// A multi-instance or containerized deployment must configure a shared, persistent Data
/// Protection key ring. Without one, a host restart loses the keys and every stored token
/// becomes undecryptable, forcing all users to authorize again (ADR-052, ADR-057).
/// </para>
/// </remarks>
public sealed class DataProtectionCalendarTokenProtector : ICalendarTokenProtector
{
    private const string Purpose = "Sirkadiyen.GoogleCalendar.RefreshToken.v1";

    private readonly IDataProtector protector;

    public DataProtectionCalendarTokenProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        protector = provider.CreateProtector(Purpose);
    }

    public string Protect(string plaintext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);
        return protector.Protect(plaintext);
    }

    public string Unprotect(string ciphertext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ciphertext);
        return protector.Unprotect(ciphertext);
    }
}
