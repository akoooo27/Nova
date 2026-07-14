using Chat.Application.AgentRuns.Queries.GetAgentRun;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

using Dapper;

using Npgsql;

namespace Chat.Infrastructure.AgentRuns.Readers;

internal sealed class AgentRunDetailReader(NpgsqlDataSource dataSource) : IAgentRunDetailReader
{
    private const string Sql = """
                               select r.kind          as "Kind",
                                      r.task          as "Task",
                                      r.started_at    as "StartedAt",
                                      r.finished_at   as "FinishedAt",
                                      r.input_tokens  as "InputTokens",
                                      r.output_tokens as "OutputTokens",
                                      (
                                          select a.title
                                          from agent_run_activities a
                                          where a.run_id = r.id and a.kind = 'Phase'
                                          order by a.sequence desc
                                          limit 1
                                      )               as "CurrentPhase"
                               from agent_runs r
                               where r.chat_id = @ChatId
                                 and r.assistant_message_id = @MessageId
                                 and r.user_id = @UserId;

                               select a.sequence    as "Sequence",
                                      a.kind        as "Kind",
                                      a.type        as "Type",
                                      a.title       as "Title",
                                      a.detail      as "Detail",
                                      a.occurred_at as "OccurredAt"
                               from agent_run_activities a
                               join agent_runs r on r.id = a.run_id
                               where r.chat_id = @ChatId
                                 and r.assistant_message_id = @MessageId
                                 and r.user_id = @UserId
                               order by a.sequence;
                               """;

    public async Task<AgentRunDetailResult?> GetAsync
    (
        ChatId chatId,
        ChatMessageId messageId,
        UserId userId,
        CancellationToken cancellationToken = default
    )
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        CommandDefinition command = new
        (
            Sql,
            new { ChatId = chatId.Value, MessageId = messageId.Value, UserId = userId.Value },
            cancellationToken: cancellationToken
        );

        await using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(command);

        SummaryRow? summary = await grid.ReadSingleOrDefaultAsync<SummaryRow>();

        if (summary is null)
        {
            return null;
        }

        ActivityRow[] activityRows = (await grid.ReadAsync<ActivityRow>()).ToArray();

        List<AgentRunActivityResult> activities = activityRows
            .Select(row => new AgentRunActivityResult
            (
                Sequence: row.Sequence,
                Kind: row.Kind,
                Type: row.Type,
                Title: row.Title,
                Detail: row.Detail,
                OccurredAt: row.OccurredAt
            ))
            .ToList();

        return new AgentRunDetailResult
        (
            Kind: summary.Kind,
            Task: summary.Task,
            CurrentPhase: summary.CurrentPhase,
            StartedAt: summary.StartedAt,
            FinishedAt: summary.FinishedAt,
            Usage: new AgentRunUsageResult(summary.InputTokens, summary.OutputTokens),
            Activities: activities
        );
    }

    private sealed record SummaryRow
    (
        string Kind,
        string Task,
        DateTime StartedAt,
        DateTime? FinishedAt,
        int InputTokens,
        int OutputTokens,
        string? CurrentPhase
    );

    private sealed record ActivityRow
    (
        int Sequence,
        string Kind,
        string Type,
        string Title,
        string? Detail,
        DateTime OccurredAt
    );
}