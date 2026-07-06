using Chat.Application.Projects.Queries.ListProjects;
using Chat.Domain.Shared;

using Dapper;

using Npgsql;

namespace Chat.Infrastructure.Projects.Readers;

internal sealed class ProjectListReader(NpgsqlDataSource dataSource) : IProjectListReader
{
    private const string Sql = """
                               select count(*)
                               from projects
                               where user_id = @UserId;

                               select
                                    id           as "Id",
                                    name         as "Name",
                                    instructions as "Instructions",
                                    emoji        as "Emoji",
                                    theme        as "Theme",
                                    created_at   as "CreatedAt",
                                    updated_at   as "UpdatedAt"
                                from projects
                                where user_id = @UserId
                                order by updated_at desc, id desc
                                limit @Limit offset @Offset;
                               """;

    public async Task<ProjectListReadModel> GetAsync
    (
        UserId userId,
        int limit,
        int offset,
        CancellationToken cancellationToken
    )
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        CommandDefinition command = new
        (
            commandText: Sql,
            parameters: new
            {
                UserId = userId.Value,
                Limit = limit,
                Offset = offset
            },
            cancellationToken: cancellationToken
        );

        await using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(command);

        int total = await grid.ReadSingleAsync<int>();
        ProjectRow[] rows = (await grid.ReadAsync<ProjectRow>()).ToArray();

        ProjectSummaryReadModel[] items = rows
            .Select(row => new ProjectSummaryReadModel
            (
                Id: row.Id,
                Name: row.Name,
                Instructions: row.Instructions,
                Emoji: row.Emoji,
                Theme: row.Theme,
                CreatedAt: row.CreatedAt,
                UpdatedAt: row.UpdatedAt
            ))
            .ToArray();

        return new ProjectListReadModel(items, total, limit, offset);
    }

    private sealed record ProjectRow
    (
        Guid Id,
        string Name,
        string? Instructions,
        string? Emoji,
        string? Theme,
        DateTime CreatedAt,
        DateTime UpdatedAt
    );
}