namespace Sirkadiyen.Api.Identity;

/// <summary>
/// Pulls the request-scoped fields an audit event records — client IP, user agent, correlation
/// id — from the current <see cref="HttpContext"/>, in one place so every call site captures them
/// the same way. The IP has already been resolved through the trusted forwarded-headers chain
/// (see <c>Program.cs</c>).
/// </summary>
internal static class AuditRequestExtensions
{
    public static string? ClientIp(this HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString();

    public static string? ClientUserAgent(this HttpContext context) =>
        context.Request.Headers.UserAgent.ToString() is { Length: > 0 } userAgent
            ? userAgent
            : null;

    /// <summary>
    /// The correlation id for this request. Prefers the value the correlation-id middleware sets,
    /// falling back to the framework trace identifier so an event always carries something.
    /// </summary>
    public static string CorrelationId(this HttpContext context) =>
        context.Items.TryGetValue(CorrelationIdKey, out object? value)
            && value is string correlationId
            && correlationId.Length > 0
                ? correlationId
                : context.TraceIdentifier;

    /// <summary>The <see cref="HttpContext.Items"/> key the correlation-id middleware writes.</summary>
    public const string CorrelationIdKey = "Sirkadiyen.CorrelationId";
}
