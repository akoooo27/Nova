using Chat.Application.Chats.Queries.GetChats;
using Chat.Domain.Shared;

using Dapper;

using Npgsql;

namespace Chat.Infrastructure.Chats.Readers;

internal sealed class ChatListReader(NpgsqlDataSource dataSource) : IChatListReader
{
    private const string Sql = """
                               select count(*)
                               from chats
                               where user_id = @UserId
                                 and is_temporary = false
                                 and is_archived = false;

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
                                  and is_temporary = false
                                  and is_archived = false
                                order by (pinned_at is null), pinned_at desc, updated_at desc, id desc
                                limit @Limit offset @Offset;
                               """;

    public async Task<ChatListReadModel> GetAsync
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
            Sql,
            new { UserId = userId.Value, Limit = limit, Offset = offset },
            cancellationToken: cancellationToken
        );

        await using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(command);

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

        return new ChatListReadModel(items, total, limit, offset);
    }

    private sealed record ChatRow
    (
        Guid Id,
        string Title,
        DateTimeOffset? PinnedAt,
        bool IsArchived,
        bool IsTemporary,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt
    );
}