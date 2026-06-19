using Chat.Application.Chats.Queries.GetChat;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

using Dapper;

using Npgsql;

namespace Chat.Infrastructure.Chats.Readers;

internal sealed class ChatDetailReader(NpgsqlDataSource dataSource) : IChatDetailReader
{
    private const string Sql = """
                               select id           as "Id",
                                      title        as "Title",
                                      pinned_at    as "PinnedAt",
                                      is_archived  as "IsArchived",
                                      is_temporary as "IsTemporary",
                                      created_at   as "CreatedAt",
                                      updated_at   as "UpdatedAt",
                                      current_message_id as "CurrentMessageId"
                               from chats
                               where id = @ChatId and user_id = @UserId;

                               select m.id               as "Id",
                                      m.parent_message_id as "ParentMessageId",
                                      m.role             as "Role",
                                      m.content          as "Content",
                                      m.status           as "Status",
                                      m.failure_reason   as "FailureReason",
                                      m.sibling_index    as "SiblingIndex",
                                      m.created_at       as "CreatedAt",
                                      m.completed_at     as "CompletedAt",
                                      m.llm_model_id     as "ModelId",
                                      lm.external_model_id as "ModelSlug",
                                      lm.name            as "ModelName"
                               from chat_messages m
                               left join llm_models lm on lm.id = m.llm_model_id
                               where m.chat_id = @ChatId
                               order by m.created_at, m.id;
                               """;

    public async Task<ChatDetailReadModel?> GetAsync
    (
        ChatId chatId,
        UserId userId,
        CancellationToken cancellationToken
    )
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        CommandDefinition command = new
        (
            Sql,
            new { ChatId = chatId.Value, UserId = userId.Value },
            cancellationToken: cancellationToken
        );

        await using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(command);

        ChatRow? chat = await grid.ReadSingleOrDefaultAsync<ChatRow>();

        if (chat is null)
        {
            return null;
        }

        MessageRow[] rows = (await grid.ReadAsync<MessageRow>()).ToArray();

        ChatMessageReadModel[] messages = rows
            .Select(row => new ChatMessageReadModel
            (
                Id: row.Id,
                ParentMessageId: row.ParentMessageId,
                Role: Enum.Parse<MessageRole>(row.Role),
                Content: row.Content,
                Status: Enum.Parse<MessageStatus>(row.Status),
                FailureReason: row.FailureReason,
                SiblingIndex: row.SiblingIndex,
                CreatedAt: row.CreatedAt,
                CompletedAt: row.CompletedAt,
                Model: row.ModelId is null
                    ? null
                    : new ChatMessageModelReadModel(row.ModelId.Value, row.ModelSlug, row.ModelName)
            ))
            .ToArray();

        return new ChatDetailReadModel
        (
            Id: chat.Id,
            Title: chat.Title,
            IsPinned: chat.PinnedAt is not null,
            PinnedAt: chat.PinnedAt,
            IsArchived: chat.IsArchived,
            IsTemporary: chat.IsTemporary,
            CreatedAt: chat.CreatedAt,
            UpdatedAt: chat.UpdatedAt,
            CurrentMessageId: chat.CurrentMessageId,
            Messages: messages
        );
    }

    private sealed record ChatRow
    (
        Guid Id,
        string Title,
        DateTime? PinnedAt,
        bool IsArchived,
        bool IsTemporary,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        Guid CurrentMessageId
    );

    private sealed record MessageRow
    (
        Guid Id,
        Guid? ParentMessageId,
        string Role,
        string? Content,
        string Status,
        string? FailureReason,
        int SiblingIndex,
        DateTime CreatedAt,
        DateTime? CompletedAt,
        Guid? ModelId,
        string? ModelSlug,
        string? ModelName
    );
}