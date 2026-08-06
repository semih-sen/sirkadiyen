namespace Sirkadiyen.Application.GoogleCalendar;

/// <summary>
/// Protects the Google refresh token at rest. Implemented in the infrastructure layer so
/// no cryptographic provider leaks into the domain or the use cases (ADR-057).
/// </summary>
public interface ICalendarTokenProtector
{
    string Protect(string plaintext);

    string Unprotect(string ciphertext);
}
