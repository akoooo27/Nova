using Chat.Api.Endpoints.Projects.Responses;
using Chat.Application.Projects.Queries.ListProjects;

namespace Chat.Api.Endpoints.Projects.ListProjects;

internal static class ResponseMapper
{
    public static Response ToResponse(ProjectListReadModel readModel) => new()
    {
        Items = readModel.Projects
            .Select(ProjectResponse.From)
            .ToList(),
        Total = readModel.Total,
        Limit = readModel.Limit,
        Offset = readModel.Offset
    };
}