using Chat.Api.Endpoints;
using Chat.Api.Endpoints.Projects.Responses;
using Chat.Application.Projects.Commands.Update;
using Chat.Application.Projects.Results;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Endpoints;

namespace Chat.Api.Endpoints.Projects.UpdateProject;

internal sealed class Request
{
    public required string Name { get; init; }

    public string? Instructions { get; init; }

    public string? Emoji { get; init; }

    public string? Theme { get; init; }
}

internal sealed class Endpoint(ISender sender) : BaseEndpoint<Request, ProjectResponse>
{
    public const string RouteName = "Chat.Projects.Update";

    public override void Configure()
    {
        Patch("/projects/{projectId}");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Update Project")
                .WithDescription("Replaces the editable state of a project.")
                .Produces<ProjectResponse>(StatusCodes.Status200OK, "application/json")
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .ProducesProblemDetails(StatusCodes.Status404NotFound, "application/json")
                .WithTags(CustomTags.Projects);
        });
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        UpdateProjectCommand command = new
        (
            ProjectId: Route<Guid>("projectId"),
            Name: request.Name,
            Instructions: request.Instructions,
            Emoji: request.Emoji,
            Theme: request.Theme
        );

        ErrorOr<ProjectResult> result = await sender.Send(command, ct);

        await SendErrorOrAsync
        (
            errorOr: result,
            mapper: ProjectResponse.From,
            cancellationToken: ct
        );
    }
}