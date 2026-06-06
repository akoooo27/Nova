using Chat.Application.FavoriteModels.Queries;
using Chat.Domain.Shared;

using Dapper;

using Npgsql;

namespace Chat.Infrastructure.FavoriteModels.Readers;

internal sealed class FavoriteModelsReader(NpgsqlDataSource dataSource) : IFavoriteModelsReader
{
    private const string Sql = """
                               select
                                    m.Id as "Id",
                                    m.external_model_id as "ExternalModelId",
                                    m.name as "Name",
                                    m.description as "Description",
                                    m.context_window as "ContextWindow",
                                    m.supports_vision as "SupportsVision",
                                    m.supports_reasoning as "SupportsReasoning",
                                    m.supports_tool_calling as "SupportsToolCalling",
                                    m.is_enabled as "IsEnabled",
                                    p.id as "ProviderId",
                                    p.name as "ProviderName",
                                    p.slug as "ProviderSlug",
                                    p.logo_key as "ProviderLogoKey"
                                from favorite_models f
                                inner join llm_models m on m.id = f.llm_model_id
                                inner join llm_providers p on p.id = m.provider_id
                                where f.user_id = @UserId
                                order by m.name, m.id;
                               """;

    public async Task<FavoriteModelsReadModel> GetAsync(UserId userId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        CommandDefinition command = new
        (
            Sql,
            new { UserId = userId.Value },
            cancellationToken: cancellationToken
        );

        FavoriteRow[] rows = (await connection.QueryAsync<FavoriteRow>(command)).ToArray();

        FavoriteLlmModelReadModel[] models = rows
            .Select(row => new FavoriteLlmModelReadModel
            (
                Id: row.Id,
                ExternalModelId: row.ExternalModelId,
                Name: row.Name,
                Description: row.Description,
                ContextWindow: row.ContextWindow,
                SupportsVision: row.SupportsVision,
                SupportsReasoning: row.SupportsReasoning,
                SupportsToolCalling: row.SupportsToolCalling,
                IsEnabled: row.IsEnabled,
                Provider: new FavoriteModelProviderReadModel
                (
                    Id: row.ProviderId,
                    Name: row.ProviderName,
                    Slug: row.ProviderSlug,
                    LogoKey: row.ProviderLogoKey
                )
            ))
            .ToArray();

        return new FavoriteModelsReadModel(models);
    }

    private sealed record FavoriteRow
    (
        Guid Id,
        string ExternalModelId,
        string Name,
        string Description,
        int ContextWindow,
        bool SupportsVision,
        bool SupportsReasoning,
        bool SupportsToolCalling,
        bool IsEnabled,
        Guid ProviderId,
        string ProviderName,
        string ProviderSlug,
        string? ProviderLogoKey
    );
}
