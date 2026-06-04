using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

using ErrorOr;

using FastEndpoints;

using IdentityIngress.Database;
using IdentityIngress.IdentityProviders;
using IdentityIngress.Options;

using Microsoft.Extensions.Options;

using Shared.Api.Endpoints;
using Shared.Api.Infrastructure;
using Shared.Application.Messaging;

namespace IdentityIngress.Endpoints.Auth0Events;

internal sealed class Endpoint(
    IdentityIngressDbContext dbContext,
    IIdentityProviderEventMapper<Request> mapper,
    IMessageBus messageBus,
    IOptions<Auth0EventsOptions> options
)
    : BaseEndpoint<Request, Response>
{
    public const string RouteName = "IdentityIngress.Auth0Events.Receive";

    public override void Configure()
    {
        Post("/api/auth0/events");
        AllowAnonymous();

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Receive Auth0 Events")
                .WithDescription("Receives Auth0 Event Stream webhook deliveries and publishes supported identity user lifecycle events.")
                .Produces<Response>(StatusCodes.Status202Accepted, "application/json")
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .WithTags(CustomTags.Auth0Events);
        });
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        if (!IsAuthorized(HttpContext.Request.Headers.Authorization, options.Value.WebhookToken))
        {
            await Send.ResultAsync(Results.Unauthorized());
            return;
        }

        ErrorOr<MappedIdentityEvent> mappingResult = mapper.Map(req);

        if (mappingResult.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(mappingResult));
            return;
        }

        MappedIdentityEvent mapping = mappingResult.Value;

        await messageBus.PublishAsync(mapping.Event, ct);
        await dbContext.SaveChangesAsync(ct);

        await Send.ResponseAsync
        (
            new Response { Accepted = true, Published = true, EventType = mapping.EventType },
            statusCode: StatusCodes.Status202Accepted,
            cancellation: ct
        );
    }

    private static bool IsAuthorized(string? header, string expectedToken)
    {
        if (!AuthenticationHeaderValue.TryParse(header, out AuthenticationHeaderValue? parsed))
            return false;

        if (!string.Equals(parsed.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
            return false;

        if (parsed.Parameter is not { Length: > 0 } token)
            return false;

        return CryptographicOperations.FixedTimeEquals
        (
            Encoding.UTF8.GetBytes(token),
            Encoding.UTF8.GetBytes(expectedToken)
        );
    }
}