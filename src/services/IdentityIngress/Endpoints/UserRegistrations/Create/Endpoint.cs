using FastEndpoints;

using IdentityIngress.Endpoints;

using MassTransit;

using Microsoft.AspNetCore.Http;

using Shared.Api.Endpoints;
using Shared.Contracts.IdentityIngress.Events;

namespace IdentityIngress.Endpoints.UserRegistrations.Create;

internal sealed class Endpoint(IPublishEndpoint publishEndpoint) : BaseEndpoint<Request, Response>
{
    public const string RouteName = "IdentityIngress.UserRegistrations.Create";

    public override void Configure()
    {
        Post("/api/user-registrations");
        AllowAnonymous();
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Create User Registration")
                .WithDescription("Accepts Auth0 user registration notifications and publishes them for downstream consumers.")
                .Produces<Response>(StatusCodes.Status202Accepted, "application/json")
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .WithTags(CustomTags.UserRegistrations);
        });
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        UserRegistered userRegistered = new()
        {
            Sub = req.Sub,
            Email = req.Email,
            EmailVerified = req.EmailVerified,
            Name = req.Name
        };

        await publishEndpoint.Publish(userRegistered, ct);

        await Send.ResponseAsync(new Response { Accepted = true }, StatusCodes.Status202Accepted, ct);
    }
}