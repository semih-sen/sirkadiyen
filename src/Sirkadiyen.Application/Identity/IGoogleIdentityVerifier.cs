namespace Sirkadiyen.Application.Identity;

public interface IGoogleIdentityVerifier
{
    Task<GoogleIdentity> VerifyAsync(
        string credential,
        CancellationToken cancellationToken);
}
