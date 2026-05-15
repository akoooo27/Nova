using System.Diagnostics;

using ErrorOr;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Shared.Api.Infrastructure;

public static class CustomResults
{
    public static IResult Problem(IReadOnlyList<Error> errors, HttpContext httpContext)
    {
        if (errors.Count == 0)
        {
            throw new InvalidOperationException("Cannot create a problem result from an empty error list.");
        }

        if (errors.All(e => e.Type == ErrorType.Validation))
            return Results.ValidationProblem
            (
                errors: GroupValidationErrors(errors),
                type: GetType(ErrorType.Validation),
                title: GetTitle(ErrorType.Validation),
                instance: GetInstance(httpContext),
                extensions: GetExtensions(httpContext)
            );

        Error first = errors[0];

        return Results.Problem
        (
            type: GetType(first.Type),
            title: GetTitle(first.Type),
            detail: first.Description,
            instance: GetInstance(httpContext),
            statusCode: GetStatusCode(first.Type),
            extensions: GetExtensions(httpContext)
        );

        static string GetType(ErrorType errorType) =>
            errorType switch
            {
                ErrorType.Validation => "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                ErrorType.NotFound => "https://tools.ietf.org/html/rfc7231#section-6.5.4",
                ErrorType.Conflict => "https://tools.ietf.org/html/rfc7231#section-6.5.8",
                ErrorType.Unauthorized => "https://tools.ietf.org/html/rfc7235#section-3.1",
                ErrorType.Forbidden => "https://tools.ietf.org/html/rfc7231#section-6.5.3",
                _ => "https://tools.ietf.org/html/rfc7231#section-6.6.1"
            };

        static string GetTitle(ErrorType errorType) =>
            errorType switch
            {
                ErrorType.Validation => "One or more validation errors occurred.",
                ErrorType.NotFound => "Resource not found.",
                ErrorType.Conflict => "Conflict.",
                ErrorType.Unauthorized => "Unauthorized.",
                ErrorType.Forbidden => "Forbidden.",
                _ => "Server failure"
            };

        static int GetStatusCode(ErrorType errorType) =>
            errorType switch
            {
                ErrorType.Validation => StatusCodes.Status400BadRequest,
                ErrorType.NotFound => StatusCodes.Status404NotFound,
                ErrorType.Conflict => StatusCodes.Status409Conflict,
                ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
                ErrorType.Forbidden => StatusCodes.Status403Forbidden,
                _ => StatusCodes.Status500InternalServerError
            };

        static string GetInstance(HttpContext httpContext) =>
            $"{httpContext.Request.Method}:{httpContext.Request.Path}";

        static Dictionary<string, object?> GetExtensions(HttpContext httpContext)
        {
            Activity? activity = httpContext.Features.Get<IHttpActivityFeature>()?.Activity;

            return new Dictionary<string, object?>
            {
                ["requestId"] = httpContext.TraceIdentifier,
                ["traceId"] = activity?.Id
            };
        }

        static Dictionary<string, string[]> GroupValidationErrors(IReadOnlyList<Error> errors) =>
            errors
                .GroupBy(e => e.Code)
                .ToDictionary
                (
                    g => g.Key,
                    g => g.Select(e => e.Description).ToArray()
                );
    }
}
