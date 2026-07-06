using Chat.Api.Endpoints.Projects.Responses;

namespace Chat.Api.Endpoints.Projects.ListProjects;

internal sealed class Response
{
    public required IReadOnlyCollection<ProjectResponse> Items { get; init; }

    public required int Total { get; init; }

    public required int Limit { get; init; }

    public required int Offset { get; init; }
}