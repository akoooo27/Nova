using ErrorOr;

using Mediator;

namespace Chat.Application.Projects.Queries.GetProjectChats;

public sealed record GetProjectChatsQuery
(
    Guid ProjectId,
    int Limit,
    int Offset
) : IQuery<ErrorOr<ProjectChatListReadModel>>;