using Chat.Api.Endpoints;
using Chat.Application.Projects.Commands.Delete;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.Projects.DeleteProject;

internal sealed class Endpoint(ISender sender) : EndpointWithoutRequest
{
    public const string RouteName = "Chat.Projects.Delete";

    public override void Configure()
    {
        Delete("/projects/{projectId}");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Delete Project")
                .WithDescription("Deletes a project owned by the authenticated user.")
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .ProducesProblemDetails(StatusCodes.Status404NotFound, "application/json")
                .WithTags(CustomTags.Projects);
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        DeleteProjectCommand command = new(Route<Guid>("projectId"));

        ErrorOr<Success> result = await sender.Send(command, ct);

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        await Send.NoContentAsync(ct);
    }
}