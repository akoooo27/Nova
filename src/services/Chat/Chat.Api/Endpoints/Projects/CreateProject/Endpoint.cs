using Chat.Api.Endpoints;
using Chat.Api.Endpoints.Projects.Responses;
using Chat.Application.Projects.Commands.Create;
using Chat.Application.Projects.Results;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.Projects.CreateProject;

internal sealed record Request
(
    string Name,
    string? Instructions,
    string? Emoji,
    string? Theme
);

internal sealed class Endpoint(ISender sender) : Endpoint<Request>
{
    public const string RouteName = "Chat.Projects.Create";

    public override void Configure()
    {
        Post("/projects");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Create Project")
                .WithDescription("Creates a project owned by the authenticated user.")
                .Produces<ProjectResponse>(StatusCodes.Status201Created)
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .WithTags(CustomTags.Projects);
        });
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        CreateProjectCommand command = new(request.Name, request.Instructions, request.Emoji, request.Theme);

        ErrorOr<ProjectResult> result = await sender.Send(command, ct);

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        ProjectResponse response = ProjectResponse.From(result.Value);

        await Send.ResultAsync(TypedResults.Created($"/v1/projects/{response.Id}", response));
    }
}