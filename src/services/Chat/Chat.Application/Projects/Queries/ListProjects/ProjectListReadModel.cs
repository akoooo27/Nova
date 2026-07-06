namespace Chat.Application.Projects.Queries.ListProjects;

public sealed record ProjectListReadModel
(
    IReadOnlyList<ProjectSummaryReadModel> Projects,
    int Total,
    int Limit,
    int Offset
);