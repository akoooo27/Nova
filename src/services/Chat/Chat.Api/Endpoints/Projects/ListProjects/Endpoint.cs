using Chat.Api.Endpoints;
using Chat.Application.Projects;
using Chat.Application.Projects.Queries.ListProjects;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.Projects.ListProjects;

internal sealed record Request
(
    [property: QueryParam] int? Limit,
    [property: QueryParam] int? Offset
);

internal sealed class Endpoint(ISender sender) : Endpoint<Request, Response>
{
    public const string RouteName = "Chat.Projects.List";

    public override void Configure()
    {
        Get("/projects");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("List Projects")
                .WithDescription("Lists the authenticated user's projects, most recently updated first.")
                .Produces<Response>(StatusCodes.Status200OK, "application/json")
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .WithTags(CustomTags.Projects);
        });
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        ListProjectsQuery query = new
        (
            Limit: request.Limit ?? ProjectLimits.DefaultQueryLimit,
            Offset: request.Offset ?? ProjectLimits.DefaultQueryOffset
        );

        ErrorOr<ProjectListReadModel> result = await sender.Send(query, ct);

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        await Send.ResponseAsync(ResponseMapper.ToResponse(result.Value), cancellation: ct);
    }
}