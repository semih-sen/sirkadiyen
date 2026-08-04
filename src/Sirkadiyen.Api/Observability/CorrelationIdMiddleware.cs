using Microsoft.Extensions.Primitives;
using Sirkadiyen.Api.Identity;

namespace Sirkadiyen.Api.Observability;

/// <summary>
/// Gives every request a correlation id — honouring an inbound <c>X-Correlation-ID</c> or minting
/// one — stores it for audit events to read, echoes it on the response, and opens a logging scope
/// so every log line for the request carries it (AI_GUIDELINE §19).
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-ID";

    private const int MaximumInboundLength = 100;

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string correlationId = ResolveCorrelationId(context);
        context.Items[AuditRequestExtensions.CorrelationIdKey] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await next(context);
        }
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out StringValues inbound)
            && inbound.Count > 0
            && inbound[0] is { Length: > 0 and <= MaximumInboundLength } supplied)
        {
            return supplied;
        }

        return context.TraceIdentifier;
    }
}
