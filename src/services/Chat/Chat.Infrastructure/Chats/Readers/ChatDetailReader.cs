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
                                      m.kind             as "Kind",
                                      m.llm_model_id     as "ModelId",
                                      lm.external_model_id as "ModelSlug",
                                      lm.name            as "ModelName"
                               from chat_messages m
                               left join llm_models lm on lm.id = m.llm_model_id
                               where m.chat_id = @ChatId
                               order by m.created_at, m.id;

                               select r.assistant_message_id as "AssistantMessageId",
                                      r.kind                 as "Kind",
                                      r.started_at           as "StartedAt",
                                      r.finished_at          as "FinishedAt",
                                      (
                                          select a.title
                                          from agent_run_activities a
                                          where a.run_id = r.id and a.kind = 'Phase'
                                          order by a.sequence desc
                                          limit 1
                                      )                      as "CurrentPhase"
                               from agent_runs r
                               where r.chat_id = @ChatId;

                               select r.assistant_message_id as "AssistantMessageId",
                                      a.type                 as "Type",
                                      count(*)::int          as "Count"
                               from agent_run_activities a
                               join agent_runs r on r.id = a.run_id
                               where r.chat_id = @ChatId
                               group by r.assistant_message_id, a.type;
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
        RunSummaryRow[] runRows = (await grid.ReadAsync<RunSummaryRow>()).ToArray();
        ActivityCountRow[] countRows = (await grid.ReadAsync<ActivityCountRow>()).ToArray();

        Dictionary<Guid, Dictionary<string, int>> countsByMessage = countRows
            .GroupBy(row => row.AssistantMessageId)
            .ToDictionary
            (
                group => group.Key,
                group => group.ToDictionary(row => row.Type, row => row.Count)
            );

        Dictionary<Guid, AgentRunSummaryReadModel> summariesByMessage = runRows.ToDictionary
        (
            row => row.AssistantMessageId,
            row => new AgentRunSummaryReadModel
            (
                Kind: row.Kind,
                CurrentPhase: row.CurrentPhase,
                ActivityCounts: countsByMessage.TryGetValue(row.AssistantMessageId, out Dictionary<string, int>? counts)
                    ? counts
                    : new Dictionary<string, int>(),
                StartedAt: row.StartedAt,
                FinishedAt: row.FinishedAt
            )
        );

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
                    : new ChatMessageModelReadModel(row.ModelId.Value, row.ModelSlug, row.ModelName),
                Kind: Enum.Parse<MessageKind>(row.Kind),
                AgentRun: summariesByMessage.TryGetValue(row.Id, out AgentRunSummaryReadModel? summary)
                    ? summary
                    : null
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
        string Kind,
        Guid? ModelId,
        string? ModelSlug,
        string? ModelName
    );

    private sealed record RunSummaryRow
    (
        Guid AssistantMessageId,
        string Kind,
        DateTime StartedAt,
        DateTime? FinishedAt,
        string? CurrentPhase
    );

    private sealed record ActivityCountRow(Guid AssistantMessageId, string Type, int Count);
}