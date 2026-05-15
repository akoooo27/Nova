using System.Diagnostics;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Shared.Api.Infrastructure;

public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        Activity? activity = httpContext.Features.Get<IHttpActivityFeature>()?.Activity;

        (int statusCode, string title, string detail, string type) = exception switch
        {
            OperationCanceledException => (
                499,
                "Client closed request.",
                "The client disconnected before the request could be completed",
                "https://httpstatuses.com/499"
            ),
            TimeoutException => (
                StatusCodes.Status504GatewayTimeout,
                "Request timed out.",
                "The request timed out while waiting for an upstream service",
                "https://tools.ietf.org/html/rfc7231#section-6.6.5"
            ),
            _ => (
                StatusCodes.Status500InternalServerError,
                "An unexpected error occurred.",
                "An unexpected error occurred",
                "https://tools.ietf.org/html/rfc7231#section-6.6.1"
            )
        };

        httpContext.Response.StatusCode = statusCode;

        if (exception is OperationCanceledException)
            logger.RequestCancelled(httpContext.TraceIdentifier, activity?.Id);
        else
            logger.UnexpectedError(exception, httpContext.TraceIdentifier, activity?.Id);

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Type = type,
                Title = title,
                Detail = detail,
                Instance = $"{httpContext.Request.Method}:{httpContext.Request.Path}",
                Status = statusCode,
                Extensions = new Dictionary<string, object?>
                {
                    ["requestId"] = httpContext.TraceIdentifier,
                    ["traceId"] = activity?.Id
                }
            }
        });
    }
}

internal static partial class GlobalExceptionHandlerLogger
{
    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Request was cancelled. RequestId: {RequestId}, TraceId: {TraceId}")]
    public static partial void RequestCancelled(this ILogger logger, string requestId, string? traceId);

    [LoggerMessage(Level = LogLevel.Error,
        Message =
            "An unexpected error occurred while processing the request. RequestId: {RequestId}, TraceId: {TraceId}")]
    public static partial void UnexpectedError(this ILogger logger, Exception exception, string requestId,
        string? traceId);
}