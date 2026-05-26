using ErrorOr;

using FastEndpoints;

using Microsoft.AspNetCore.Http;

using Shared.Api.Infrastructure;

namespace Shared.Api.Endpoints;

public abstract class BaseEndpoint<TRequest, TResponse> : Endpoint<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : notnull
{
    protected async Task SendErrorOrAsync<TValue>
    (
        ErrorOr<TValue> errorOr,
        Func<TValue, TResponse> mapper,
        int successStatusCode = StatusCodes.Status200OK,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(mapper);

        if (errorOr.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(errorOr));
            return;
        }

        TResponse response = mapper(errorOr.Value);

        await Send.ResponseAsync(response, successStatusCode, cancellationToken);
    }
}

public abstract class BaseEndpoint<TRequest> : Endpoint<TRequest>
    where TRequest : notnull
{
    protected async Task SendErrorOrAsync<TValue>
    (
        ErrorOr<TValue> errorOr,
        CancellationToken cancellationToken = default
    )
    {
        if (errorOr.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(errorOr));
            return;
        }

        await Send.NoContentAsync(cancellationToken);
    }
}