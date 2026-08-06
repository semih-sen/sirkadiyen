namespace Sirkadiyen.Application.Licensing;

public sealed class LicenseHashCollisionException(string message) : Exception(message);
