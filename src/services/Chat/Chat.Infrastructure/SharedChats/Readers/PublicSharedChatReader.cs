using Chat.Application.SharedChats.Queries.GetPublicSharedChat;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.SharedChats.ValueObjects;

using Dapper;

using Npgsql;

namespace Chat.Infrastructure.SharedChats.Readers;

internal sealed class PublicSharedChatReader(NpgsqlDataSource dataSource) : IPublicSharedChatReader
{
    private const string Sql = """
                               select
                                   id                 as "Id",
                                   title              as "Title",
                                   created_at         as "CreatedAt",
                                   current_message_id as "CurrentMessageId",
                                   allow_remix        as "AllowRemix"
                               from shared_chats
                               where id = @SharedChatId;

                               with recursive path as
                               (
                                   select
                                       message.id,
                                       message.chat_id,
                                       message.parent_message_id,
                                       message.role,
                                       message.content,
                                       message.status,
                                       message.created_at,
                                       message.completed_at,
                                       0 as depth,
                                       array[message.id] as visited
                                   from shared_chats shared_chat
                                   join chat_messages message
                                     on message.chat_id = shared_chat.chat_id
                                    and message.id = shared_chat.current_message_id
                                   where shared_chat.id = @SharedChatId

                                   union all

                                   select
                                       parent.id,
                                       parent.chat_id,
                                       parent.parent_message_id,
                                       parent.role,
                                       parent.content,
                                       parent.status,
                                       parent.created_at,
                                       parent.completed_at,
                                       child.depth + 1,
                                       child.visited || parent.id
                                   from path child
                                   join chat_messages parent
                                     on parent.chat_id = child.chat_id
                                    and parent.id = child.parent_message_id
                                   where not parent.id = any(child.visited)
                               )
                               select
                                   id                as "Id",
                                   parent_message_id as "ParentMessageId",
                                   role              as "Role",
                                   content           as "Content",
                                   status            as "Status",
                                   created_at        as "CreatedAt",
                                   completed_at      as "CompletedAt"
                               from path
                               order by depth desc;
                               """;

    public async Task<PublicSharedChatReadModel?> GetAsync
    (
        SharedChatId id,
        CancellationToken cancellationToken
    )
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        CommandDefinition command = new
        (
            commandText: Sql,
            parameters: new { SharedChatId = id.Value },
            cancellationToken: cancellationToken
        );

        await using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(command);

        SharedChatRow? sharedChat = await grid.ReadSingleOrDefaultAsync<SharedChatRow>();
        MessageRow[] rows = (await grid.ReadAsync<MessageRow>()).ToArray();

        if (sharedChat is null)
        {
            return null;
        }

        PublicSharedChatMessageReadModel[] messages = rows
            .Select(row => new PublicSharedChatMessageReadModel
            (
                Id: row.Id,
                ParentMessageId: row.ParentMessageId,
                Role: Enum.Parse<MessageRole>(row.Role),
                Content: row.Content,
                Status: Enum.Parse<MessageStatus>(row.Status),
                CreatedAt: row.CreatedAt,
                CompletedAt: row.CompletedAt
            ))
            .ToArray();

        ValidatePath(sharedChat, messages);

        return new PublicSharedChatReadModel
        (
            Id: sharedChat.Id,
            Title: sharedChat.Title,
            CreatedAt: sharedChat.CreatedAt,
            CurrentMessageId: sharedChat.CurrentMessageId,
            AllowRemix: sharedChat.AllowRemix,
            Messages: messages
        );
    }

    private static void ValidatePath
    (
        SharedChatRow sharedChat,
        PublicSharedChatMessageReadModel[] messages
    )
    {
        if (messages.Length == 0
            || messages[0].ParentMessageId is not null
            || messages[0].Role != MessageRole.User
            || messages[0].Status != MessageStatus.Completed
            || messages[^1].Id != sharedChat.CurrentMessageId
            || messages.Select(message => message.Id).Distinct().Count() != messages.Length)
        {
            throw new InvalidOperationException("Shared chat ancestry is invalid.");
        }

        for (int index = 1; index < messages.Length; index++)
        {
            if (messages[index].ParentMessageId != messages[index - 1].Id)
            {
                throw new InvalidOperationException("Shared chat ancestry is invalid.");
            }
        }
    }

    private sealed record SharedChatRow
    (
        Guid Id,
        string Title,
        DateTime CreatedAt,
        Guid CurrentMessageId,
        bool AllowRemix
    );

    private sealed record MessageRow
    (
        Guid Id,
        Guid? ParentMessageId,
        string Role,
        string? Content,
        string Status,
        DateTime CreatedAt,
        DateTime? CompletedAt
    );
}