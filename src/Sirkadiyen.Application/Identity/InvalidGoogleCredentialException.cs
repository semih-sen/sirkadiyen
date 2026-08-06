namespace Sirkadiyen.Application.Identity;

public sealed class InvalidGoogleCredentialException(string message, Exception? inner = null)
    : Exception(message, inner);
