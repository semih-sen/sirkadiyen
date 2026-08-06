namespace Sirkadiyen.Application.Auditing;

/// <summary>Encrypts and decrypts the full client IP kept behind a masked audit record.</summary>
public interface IAuditIpProtector
{
    string Protect(string plaintextIp);

    string Unprotect(string ciphertext);
}
