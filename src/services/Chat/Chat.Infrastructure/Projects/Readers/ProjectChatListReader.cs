using Chat.Application.Chats.Queries.GetChats;
using Chat.Application.Projects.Queries.GetProjectChats;
using Chat.Domain.Projects.ValueObjects;
using Chat.Domain.Shared;

using Dapper;

using Npgsql;

namespace Chat.Infrastructure.Projects.Readers;

internal sealed class ProjectChatListReader(NpgsqlDataSource dataSource) : IProjectChatListReader
{
    private const string Sql = """
                               select exists
                               (
                                    select 1
                                    from projects
                                    where id = @ProjectId
                                      and user_id = @UserId
                               );

                               select count(*)
                               from chats
                               where user_id = @UserId
                                 and project_id = @ProjectId
                                 and is_temporary = false;

                               select
                                    id           as "Id",
                                    title        as "Title",
                                    pinned_at    as "PinnedAt",
                                    is_archived  as "IsArchived",
                                    is_temporary as "IsTemporary",
                                    created_at   as "CreatedAt",
                                    updated_at   as "UpdatedAt"
                                from chats
                                where user_id = @UserId
                                  and project_id = @ProjectId
                                  and is_temporary = false
                                order by (pinned_at is null), pinned_at desc, updated_at desc, id desc
                                limit @Limit offset @Offset;
                               """;

    public async Task<ProjectChatListReadModel?> GetAsync
    (
        ProjectId projectId,
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
                ProjectId = projectId.Value,
                UserId = userId.Value,
                Limit = limit,
                Offset = offset
            },
            cancellationToken: cancellationToken
        );

        await using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(command);

        bool projectExists = await grid.ReadSingleAsync<bool>();

        if (!projectExists)
        {
            return null;
        }

        int total = await grid.ReadSingleAsync<int>();
        ChatRow[] rows = (await grid.ReadAsync<ChatRow>()).ToArray();

        ChatSummaryReadModel[] items = rows
            .Select(row => new ChatSummaryReadModel
            (
                Id: row.Id,
                Title: row.Title,
                IsPinned: row.PinnedAt is not null,
                PinnedAt: row.PinnedAt,
                IsArchived: row.IsArchived,
                IsTemporary: row.IsTemporary,
                CreatedAt: row.CreatedAt,
                UpdatedAt: row.UpdatedAt
            ))
            .ToArray();

        return new ProjectChatListReadModel(items, total, limit, offset);
    }

    private sealed record ChatRow
    (
        Guid Id,
        string Title,
        DateTime? PinnedAt,
        bool IsArchived,
        bool IsTemporary,
        DateTime CreatedAt,
        DateTime UpdatedAt
    );
}