namespace Sirkadiyen.Application.Identity;

public sealed class GoogleIdentityConflictException(string message) : Exception(message);
