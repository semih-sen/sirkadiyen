using System.Security.Cryptography;
using System.Text;

namespace Sirkadiyen.Api.Administration;

/// <summary>The shared secret that guards the administrative endpoints.</summary>
/// <remarks>
/// This is a placeholder for real authentication, not a substitute for it. There
/// is no identity provider yet (ADR-022 onwards are unimplemented), and the
/// approval endpoint can put a quarantined schedule into student calendars, so
/// leaving it open was never an option. The key is required configuration: the
/// API refuses to start without one rather than defaulting to no protection.
/// </remarks>
public sealed record AdminApiOptions
{
    public const string HeaderName = "X-Admin-Api-Key";

    public required string ApiKey { get; init; }
}

/// <summary>Rejects an administrative request that does not carry the key.</summary>
public sealed class AdminApiKeyFilter(AdminApiOptions options) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        string? presented = context.HttpContext.Request.Headers[AdminApiOptions.HeaderName];

        // Compared in fixed time so a wrong key cannot be recovered one byte at a
        // time. The key itself is never logged or echoed back.
        if (presented is null
            || !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(presented),
                Encoding.UTF8.GetBytes(options.ApiKey)))
        {
            return Results.Problem(
                title: "Unauthorized",
                detail: $"A valid '{AdminApiOptions.HeaderName}' header is required.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        return await next(context);
    }
}
