using ErrorOr;

using Microsoft.AspNetCore.Http;

namespace Shared.Api.Infrastructure;

public static class CustomResults
{
    public static IResult Problem<TValue>(ErrorOr<TValue> errorOr)
    {
        if (!errorOr.IsError)
        {
            throw new InvalidOperationException("Cannot create a problem result from a successful ErrorOr.");
        }

        List<Error> errors = errorOr.Errors;

        if (errors.TrueForAll(static error => error.Type == ErrorType.Validation))
        {
            return Results.ValidationProblem
            (
                errors: errors
                    .GroupBy(error => error.Code)
                    .ToDictionary
                    (
                        group => group.Key,
                        group => group.Select(static error => error.Description).ToArray()
                    ),
                title: "One or more validation errors occurred",
                type: GetType(ErrorType.Validation),
                statusCode: GetStatusCode(ErrorType.Validation));
        }

        Error primary = errors[0];

        return Results.Problem
        (
            title: GetTitle(primary),
            detail: GetDetail(primary),
            type: GetType(primary.Type),
            statusCode: GetStatusCode(primary.Type),
            extensions: GetExtensions(errors)
        );
    }

    private static string GetTitle(Error error) =>
        error.Type switch
        {
            ErrorType.Failure or
            ErrorType.Validation or
            ErrorType.NotFound or
            ErrorType.Conflict or
            ErrorType.Unauthorized or
            ErrorType.Forbidden => error.Code,
            _ => "Server failure"
        };

    private static string GetDetail(Error error) =>
        error.Type switch
        {
            ErrorType.Failure or
            ErrorType.Validation or
            ErrorType.NotFound or
            ErrorType.Conflict or
            ErrorType.Unauthorized or
            ErrorType.Forbidden => error.Description,
            _ => "An unexpected error occurred"
        };

    private static string GetType(ErrorType errorType) =>
        errorType switch
        {
            ErrorType.Failure => "https://tools.ietf.org/html/rfc7231#section-6.5.1",
            ErrorType.Validation => "https://tools.ietf.org/html/rfc7231#section-6.5.1",
            ErrorType.NotFound => "https://tools.ietf.org/html/rfc7231#section-6.5.4",
            ErrorType.Conflict => "https://tools.ietf.org/html/rfc7231#section-6.5.8",
            ErrorType.Unauthorized => "https://tools.ietf.org/html/rfc7235#section-3.1",
            ErrorType.Forbidden => "https://tools.ietf.org/html/rfc7231#section-6.5.3",
            _ => "https://tools.ietf.org/html/rfc7231#section-6.6.1"
        };

    private static int GetStatusCode(ErrorType errorType) =>
        errorType switch
        {
            ErrorType.Failure => StatusCodes.Status400BadRequest,
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status500InternalServerError
        };

    private static Dictionary<string, object?> GetExtensions(IReadOnlyList<Error> errors) =>
        new()
        {
            ["errors"] = errors
                .Select(static error => new
                {
                    error.Code,
                    error.Description,
                    Type = error.Type.ToString()
                })
                .ToArray()
        };
}