namespace Sirkadiyen.Application.Licensing;

public interface ILicenseCodeService
{
    GeneratedLicenseCode Generate();

    bool TryHash(string plaintextCode, out byte[] codeHash);
}
