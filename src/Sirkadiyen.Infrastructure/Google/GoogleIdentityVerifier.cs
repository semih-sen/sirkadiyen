using Google.Apis.Auth;
using Sirkadiyen.Application.Identity;

namespace Sirkadiyen.Infrastructure.Google;

/// <summary>Validates Google Identity Services ID credentials server-side.</summary>
public sealed class GoogleIdentityVerifier(GoogleSignInOptions options)
    : IGoogleIdentityVerifier
{
    public async Task<GoogleIdentity> VerifyAsync(
        string credential,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credential);

        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(
                    credential,
                    new GoogleJsonWebSignature.ValidationSettings
                    {
                        Audience = [options.ClientId],
                    })
                .WaitAsync(cancellationToken);
        }
        catch (InvalidJwtException exception)
        {
            throw new InvalidGoogleCredentialException(
                "The Google credential is invalid or expired.",
                exception);
        }

        if (string.IsNullOrWhiteSpace(payload.Subject)
            || string.IsNullOrWhiteSpace(payload.Email))
        {
            throw new InvalidGoogleCredentialException(
                "The Google credential does not contain the required identity claims.");
        }

        return new GoogleIdentity
        {
            Subject = payload.Subject,
            Email = payload.Email,
            EmailVerified = payload.EmailVerified,
            DisplayName = payload.Name,
        };
    }
}
