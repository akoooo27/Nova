using ErrorOr;

using Mediator;

namespace Chat.Application.Projects.Queries.ListProjects;

public sealed record ListProjectsQuery
(
    int Limit,
    int Offset
) : IQuery<ErrorOr<ProjectListReadModel>>;