using BasisOps.Application.Abstractions;
using BasisOps.Application.Ports;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace BasisOps.Api.Http;

/// <summary>Turns known exceptions into ProblemDetails; everything else stays a 500.</summary>
internal sealed partial class ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            BadHttpRequestException bad => (bad.StatusCode, "Bad request"),
            PersistenceConflictException => (StatusCodes.Status409Conflict, "Conflict"),
            SapUnavailableException => (StatusCodes.Status503ServiceUnavailable, "SAP unavailable"),
            _ => (0, string.Empty),
        };

        if (status == 0)
        {
            return false;
        }

        LogHandled(exception, status);
        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = exception.Message },
        });
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Request failed with {StatusCode}")]
    private partial void LogHandled(Exception exception, int statusCode);
}
