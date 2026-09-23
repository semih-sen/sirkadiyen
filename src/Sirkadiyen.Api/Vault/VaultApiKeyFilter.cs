using System.Security.Cryptography;
using System.Text;

namespace Sirkadiyen.Api.Vault;

/// <summary>
/// Admits a vault request only when it carries the configured key. The vault endpoints are called by
/// an iPad shortcut, not by the signed-in web app, so they sit outside cookie authentication and
/// this key is their whole access control (ADR-168).
/// </summary>
internal sealed class VaultApiKeyFilter(VaultApiOptions options) : IEndpointFilter
{
    public const string HeaderName = "X-Vault-Key";

    private readonly byte[] expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(options.ApiKey));

    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        string? presented = context.HttpContext.Request.Headers[HeaderName];

        // Comparing fixed-length hashes in constant time reveals neither the key's content nor its
        // length through response timing.
        byte[] presentedHash = SHA256.HashData(Encoding.UTF8.GetBytes(presented ?? string.Empty));
        return presented is { Length: > 0 } && CryptographicOperations.FixedTimeEquals(presentedHash, expectedHash)
            ? next(context)
            : ValueTask.FromResult<object?>(TypedResults.Unauthorized());
    }
}
