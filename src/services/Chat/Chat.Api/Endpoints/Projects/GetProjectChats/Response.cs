namespace Chat.Api.Endpoints.Projects.GetProjectChats;

internal sealed class Response
{
    public required IReadOnlyCollection<ProjectChatResponse> Items { get; init; }

    public required int Total { get; init; }

    public required int Limit { get; init; }

    public required int Offset { get; init; }
}