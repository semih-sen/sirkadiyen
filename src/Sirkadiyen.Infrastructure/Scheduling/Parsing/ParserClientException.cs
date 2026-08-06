using System.Net;

namespace Sirkadiyen.Infrastructure.ScheduleParsing;

public sealed class ParserClientException(HttpStatusCode statusCode, string responseBody)
    : Exception($"Parser HTTP request failed with status {(int)statusCode}: {responseBody}")
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
