using Microsoft.AspNetCore.DataProtection;
using Sirkadiyen.Application.Auditing;

namespace Sirkadiyen.Infrastructure.Security;

/// <summary>
/// Encrypts the full client IP kept behind a masked audit record, using ASP.NET Core Data
/// Protection with a purpose distinct from every other protected payload.
/// </summary>
/// <remarks>
/// The purpose string binds the ciphertext to this use, so a value protected here cannot be
/// unprotected as a calendar token or session payload. Like the calendar-token protector it depends
/// on a shared, persistent key ring in a multi-instance deployment (ADR-058).
/// </remarks>
public sealed class DataProtectionAuditIpProtector : IAuditIpProtector
{
    private const string Purpose = "Sirkadiyen.Audit.ClientIp.v1";

    private readonly IDataProtector protector;

    public DataProtectionAuditIpProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        protector = provider.CreateProtector(Purpose);
    }

    public string Protect(string plaintextIp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintextIp);
        return protector.Protect(plaintextIp);
    }

    public string Unprotect(string ciphertext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ciphertext);
        return protector.Unprotect(ciphertext);
    }
}
