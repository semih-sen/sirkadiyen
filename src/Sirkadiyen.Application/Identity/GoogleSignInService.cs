using Sirkadiyen.Domain.Identity;

namespace Sirkadiyen.Application.Identity;

/// <summary>Verifies Google identity and establishes the corresponding local user.</summary>
public sealed class GoogleSignInService(
    IGoogleIdentityVerifier identityVerifier,
    IUserStore userStore,
    TimeProvider timeProvider)
{
    // ADR-045: backend-owned bootstrap data, never accepted from a client claim.
    public const string SuperAdminEmail = "halil.semih.sen@gmail.com";

    public async Task<UserSession> SignInAsync(
        string credential,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credential);

        GoogleIdentity identity = await identityVerifier.VerifyAsync(
            credential,
            cancellationToken);

        if (!identity.EmailVerified)
        {
            throw new InvalidGoogleCredentialException(
                "Google did not verify the email address.");
        }

        identity = identity with
        {
            Subject = identity.Subject?.Trim() ?? string.Empty,
            Email = identity.Email?.Trim() ?? string.Empty,
            DisplayName = string.IsNullOrWhiteSpace(identity.DisplayName)
                ? null
                : identity.DisplayName.Trim(),
        };

        if (identity.Subject.Length is 0 or > User.MaximumGoogleSubjectLength
            || identity.DisplayName?.Length > User.MaximumDisplayNameLength)
        {
            throw new InvalidGoogleCredentialException(
                "Google returned invalid identity profile fields.");
        }

        string normalizedEmail;
        try
        {
            normalizedEmail = User.NormalizeEmailValue(identity.Email);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidGoogleCredentialException(
                "Google returned an invalid email address.",
                exception);
        }

        UserRole bootstrapRole = string.Equals(
            normalizedEmail,
            User.NormalizeEmailValue(SuperAdminEmail),
            StringComparison.Ordinal)
                ? UserRole.SuperAdmin
                : UserRole.User;

        return await userStore.SignInWithGoogleAsync(
            identity,
            bootstrapRole,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }
}
