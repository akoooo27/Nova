using Chat.Application.ModelCatalog.LlmProviders.Queries.GetPublicModelCatalog;

using Dapper;

using Npgsql;

namespace Chat.Infrastructure.ModelCatalog.Readers;

internal sealed class PublicModelCatalogDapperReader(NpgsqlDataSource dataSource)
{
    private const string Sql = """
                               select
                                   p.id as "Id",
                                   p.name as "Name",
                                   p.slug as "Slug",
                                   p.is_featured as "IsFeatured",
                                   p.logo_key as "LogoKey"
                               from llm_providers p
                               where exists (
                                   select 1
                                   from llm_models m
                                   where m.provider_id = p.id
                                     and m.is_enabled
                               )
                               order by p.is_featured desc, p.name, p.id;

                               select
                                   m.id as "Id",
                                   m.provider_id as "ProviderId",
                                   m.external_model_id as "ExternalModelId",
                                   m.name as "Name",
                                   m.description as "Description",
                                   m.context_window as "ContextWindow",
                                   m.supports_vision as "SupportsVision",
                                   m.supports_reasoning as "SupportsReasoning",
                                   m.supports_tool_calling as "SupportsToolCalling"
                               from llm_models m
                               where m.is_enabled
                               order by m.provider_id, m.name, m.id;
                               """;

    public async Task<PublicModelCatalogReadModel> GetAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        CommandDefinition command = new(Sql, cancellationToken: cancellationToken);

        await using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(command);

        IReadOnlyList<ProviderRow> providerRows = (await grid.ReadAsync<ProviderRow>()).AsList();

        IReadOnlyList<ModelRow> modelRows = (await grid.ReadAsync<ModelRow>()).AsList();

        ILookup<Guid, ModelRow> modelsByProvider = modelRows.ToLookup(model => model.ProviderId);

        PublicLlmProviderReadModel[] providers = providerRows
            .Select(provider => new PublicLlmProviderReadModel
            (
                Id: provider.Id,
                Name: provider.Name,
                Slug: provider.Slug,
                IsFeatured: provider.IsFeatured,
                LogoKey: provider.LogoKey,
                Models: modelsByProvider[provider.Id]
                    .Select(model => new PublicLlmModelReadModel
                    (
                        Id: model.Id,
                        ProviderId: model.ProviderId,
                        ExternalModelId: model.ExternalModelId,
                        Name: model.Name,
                        Description: model.Description,
                        ContextWindow: model.ContextWindow,
                        SupportsVision: model.SupportsVision,
                        SupportsReasoning: model.SupportsReasoning,
                        SupportsToolCalling: model.SupportsToolCalling
                    ))
                    .ToArray()
            ))
            .ToArray();

        return new PublicModelCatalogReadModel(providers);
    }

    private sealed record ProviderRow
    (
        Guid Id,
        string Name,
        string Slug,
        bool IsFeatured,
        string? LogoKey
    );

    private sealed record ModelRow
    (
        Guid Id,
        Guid ProviderId,
        string ExternalModelId,
        string Name,
        string Description,
        int ContextWindow,
        bool SupportsVision,
        bool SupportsReasoning,
        bool SupportsToolCalling
    );
}