using Chat.Application.SharedChats.Queries.GetSharedChats;
using Chat.Domain.Shared;

using Dapper;

using Npgsql;

namespace Chat.Infrastructure.SharedChats.Readers;

internal sealed class SharedChatListReader(NpgsqlDataSource dataSource) : ISharedChatListReader
{
    private const string Sql = """
                               select count(*)
                               from shared_chats
                               where user_id = @UserId;

                               select
                                   id                 as "Id",
                                   title              as "Title",
                                   chat_id            as "ChatId",
                                   current_message_id as "CurrentMessageId",
                                   created_at         as "CreatedAt"
                               from shared_chats
                               where user_id = @UserId
                               order by created_at desc, id desc
                               limit @Limit offset @Offset;
                               """;

    public async Task<SharedChatListReadModel> GetAsync
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

        long total = await grid.ReadSingleAsync<long>();
        SharedChatRow[] rows = (await grid.ReadAsync<SharedChatRow>()).ToArray();

        SharedChatSummaryReadModel[] sharedChats = rows
            .Select(row => new SharedChatSummaryReadModel
            (
                Id: row.Id,
                Title: row.Title,
                ChatId: row.ChatId,
                CurrentMessageId: row.CurrentMessageId,
                CreatedAt: row.CreatedAt
            ))
            .ToArray();

        return new SharedChatListReadModel
        (
            SharedChats: sharedChats,
            Total: checked((int)total),
            Limit: limit,
            Offset: offset
        );
    }

    private sealed record SharedChatRow
    (
        Guid Id,
        string Title,
        Guid ChatId,
        Guid CurrentMessageId,
        DateTime CreatedAt
    );
}