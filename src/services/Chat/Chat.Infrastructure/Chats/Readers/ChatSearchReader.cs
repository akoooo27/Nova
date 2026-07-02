using Chat.Application.Chats.Queries.SearchChats;
using Chat.Domain.Shared;

using Dapper;

using Npgsql;

namespace Chat.Infrastructure.Chats.Readers;

internal sealed class ChatSearchReader(NpgsqlDataSource dataSource) : IChatSearchReader
{
    private const string Sql = """
                               with search_query as (
                                   select websearch_to_tsquery('simple', @Query) as query
                               )
                               select count(*)::int
                               from chats
                               cross join search_query
                               where chats.user_id = @UserId
                                 and chats.is_temporary = false
                                 and chats.is_archived = @IsArchived
                                 and (
                                       to_tsvector('simple', chats.title) @@ search_query.query
                                       or exists
                                       (
                                           select 1
                                           from chat_messages messages
                                           where messages.chat_id = chats.id
                                             and messages.status = 'Completed'
                                             and messages.search_vector @@ search_query.query
                                       )
                                     );

                               with search_query as (
                                   select websearch_to_tsquery('simple', @Query) as query
                               ),
                               message_stats as (
                                   select
                                        messages.chat_id,
                                        count(*)::int                                            as match_count,
                                        max(ts_rank(messages.search_vector, search_query.query)) as best_rank
                                   from chat_messages messages
                                   cross join search_query
                                   join chats on chats.id = messages.chat_id
                                   where chats.user_id = @UserId
                                     and chats.is_temporary = false
                                     and chats.is_archived = @IsArchived
                                     and messages.status = 'Completed'
                                     and messages.search_vector @@ search_query.query
                                   group by messages.chat_id
                               ),
                               page as (
                                   select
                                        chats.id,
                                        chats.title,
                                        chats.pinned_at,
                                        chats.is_archived,
                                        chats.created_at,
                                        chats.updated_at,
                                        coalesce(message_stats.match_count, 0) as match_count,
                                        (2.0 * ts_rank(to_tsvector('simple', chats.title), search_query.query))
                                            + coalesce(message_stats.best_rank, 0)
                                            + (0.05 * least(coalesce(message_stats.match_count, 0), 20)) as score
                                   from chats
                                   cross join search_query
                                   left join message_stats on message_stats.chat_id = chats.id
                                   where chats.user_id = @UserId
                                     and chats.is_temporary = false
                                     and chats.is_archived = @IsArchived
                                     and (
                                           message_stats.chat_id is not null
                                           or to_tsvector('simple', chats.title) @@ search_query.query
                                         )
                                   order by score desc, chats.updated_at desc, chats.id desc
                                   limit @Limit offset @Offset
                               )
                               select
                                    page.id             as "Id",
                                    page.title          as "Title",
                                    page.pinned_at      as "PinnedAt",
                                    page.is_archived    as "IsArchived",
                                    page.created_at     as "CreatedAt",
                                    page.updated_at     as "UpdatedAt",
                                    page.match_count    as "MatchCount",
                                    snippets.message_id as "MessageId",
                                    snippets.role       as "Role",
                                    snippets.snippet    as "Snippet"
                               from page
                               cross join search_query
                               left join lateral
                               (
                                   select
                                        messages.id   as message_id,
                                        messages.role as role,
                                        ts_rank(messages.search_vector, search_query.query) as snippet_rank,
                                        ts_headline
                                        (
                                            'simple',
                                            messages.content,
                                            search_query.query,
                                            'MaxFragments=1, MaxWords=18, MinWords=6, StartSel="", StopSel=""'
                                        ) as snippet
                                   from chat_messages messages
                                   where messages.chat_id = page.id
                                     and messages.status = 'Completed'
                                     and messages.search_vector @@ search_query.query
                                   order by snippet_rank desc, messages.created_at desc, messages.id desc
                                   limit 3
                               ) snippets on true
                               order by page.score desc, page.updated_at desc, page.id desc, snippets.snippet_rank desc nulls last;
                               """;

    public async Task<ChatSearchReadModel> SearchAsync
    (
        UserId userId,
        string query,
        bool isArchived,
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
                Query = query,
                IsArchived = isArchived,
                Limit = limit,
                Offset = offset
            },
            cancellationToken: cancellationToken
        );

        await using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(command);

        int total = await grid.ReadSingleAsync<int>();
        ChatRow[] rows = (await grid.ReadAsync<ChatRow>()).ToArray();

        ChatSearchResultReadModel[] chats = rows
            .GroupBy(row => row.Id)
            .Select(group =>
            {
                ChatRow first = group.First();

                return new ChatSearchResultReadModel
                (
                    Id: first.Id,
                    Title: first.Title,
                    IsPinned: first.PinnedAt is not null,
                    PinnedAt: first.PinnedAt,
                    IsArchived: first.IsArchived,
                    CreatedAt: first.CreatedAt,
                    UpdatedAt: first.UpdatedAt,
                    MatchCount: first.MatchCount,
                    Snippets: group
                        .Where(row => row.MessageId is not null)
                        .Select(row => new ChatSearchSnippetReadModel
                        (
                            MessageId: row.MessageId!.Value,
                            Role: row.Role!,
                            Text: row.Snippet ?? string.Empty
                        ))
                        .ToArray()
                );
            })
            .ToArray();

        return new ChatSearchReadModel
        (
            Chats: chats,
            Total: total,
            Limit: limit,
            Offset: offset
        );
    }

    private sealed record ChatRow
    (
        Guid Id,
        string Title,
        DateTime? PinnedAt,
        bool IsArchived,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        int MatchCount,
        Guid? MessageId,
        string? Role,
        string? Snippet
    );
}