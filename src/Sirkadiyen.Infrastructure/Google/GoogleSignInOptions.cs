namespace Sirkadiyen.Infrastructure.Google;

public sealed record GoogleSignInOptions
{
    public required string ClientId { get; init; }
}
