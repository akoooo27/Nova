using Chat.Application.ModelCatalog.LlmProviders.Queries.GetManagedModelCatalog;

using Dapper;

using Npgsql;

namespace Chat.Infrastructure.ModelCatalog.Readers;

internal sealed class ManagedModelCatalogDapperReader(NpgsqlDataSource dataSource)
    : IManagedModelCatalogReader
{
    private const string Sql = """
                               select
                                   p.id as "Id",
                                   p.name as "Name",
                                   p.slug as "Slug",
                                   p.is_featured as "IsFeatured",
                                   p.is_enabled as "IsEnabled",
                                   p.logo_key as "LogoKey"
                               from llm_providers p
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
                                   m.supports_tool_calling as "SupportsToolCalling",
                                   m.is_enabled as "IsEnabled"
                               from llm_models m
                               order by m.provider_id, m.name, m.id;
                               """;

    public async Task<ManagedModelCatalogReadModel> GetAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        CommandDefinition command = new(Sql, cancellationToken: cancellationToken);

        await using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(command);

        IReadOnlyList<ProviderRow> providerRows = (await grid.ReadAsync<ProviderRow>()).AsList();

        IReadOnlyList<ModelRow> modelRows = (await grid.ReadAsync<ModelRow>()).AsList();

        ILookup<Guid, ModelRow> modelsByProvider = modelRows.ToLookup(model => model.ProviderId);

        ManagedLlmProviderReadModel[] providers = providerRows
            .Select(provider => new ManagedLlmProviderReadModel
            (
                Id: provider.Id,
                Name: provider.Name,
                Slug: provider.Slug,
                IsFeatured: provider.IsFeatured,
                IsEnabled: provider.IsEnabled,
                LogoKey: provider.LogoKey,
                Models: modelsByProvider[provider.Id]
                    .Select(model => new ManagedLlmModelReadModel
                    (
                        Id: model.Id,
                        ProviderId: model.ProviderId,
                        ExternalModelId: model.ExternalModelId,
                        Name: model.Name,
                        Description: model.Description,
                        ContextWindow: model.ContextWindow,
                        SupportsVision: model.SupportsVision,
                        SupportsReasoning: model.SupportsReasoning,
                        SupportsToolCalling: model.SupportsToolCalling,
                        IsEnabled: model.IsEnabled
                    ))
                    .ToArray()
            ))
            .ToArray();

        return new ManagedModelCatalogReadModel(providers);
    }

    private sealed record ProviderRow
    (
        Guid Id,
        string Name,
        string Slug,
        bool IsFeatured,
        bool IsEnabled,
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
        bool SupportsToolCalling,
        bool IsEnabled
    );
}