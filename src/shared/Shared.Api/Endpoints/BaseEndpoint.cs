using ErrorOr;

using FastEndpoints;

using Shared.Api.Infrastructure;

namespace Shared.Api.Endpoints;

internal abstract class BaseEndpoint<TRequest, TResponse> : Endpoint<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : notnull
{
    protected async Task SendErrorOrAsync<T>
    (
        ErrorOr<T> errorOr,
        Func<T, TResponse> mapper,
        int successStatusCode = 200,
        CancellationToken cancellationToken = default
    )
    {
        if (errorOr.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(errorOr.Errors, HttpContext));
            return;
        }

        TResponse response = mapper(errorOr.Value);

        await Send.ResponseAsync(response, successStatusCode, cancellationToken);
    }
}

internal abstract class BaseEndpoint<TRequest> : Endpoint<TRequest>
    where TRequest : notnull
{
    protected async Task SendErrorOrAsync
    (
        ErrorOr<Success> errorOr,
        CancellationToken cancellationToken = default
    )
    {
        if (errorOr.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(errorOr.Errors, HttpContext));
            return;
        }

        await Send.NoContentAsync(cancellationToken);
    }
}