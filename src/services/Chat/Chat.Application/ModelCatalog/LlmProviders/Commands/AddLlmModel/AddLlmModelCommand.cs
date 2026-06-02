using Chat.Application.ModelCatalog.LlmProviders.Results;

using ErrorOr;

using Mediator;

namespace Chat.Application.ModelCatalog.LlmProviders.Commands.AddLlmModel;

public sealed record AddLlmModelCommand
(
    Guid ProviderId,
    string ExternalModelId,
    string Name,
    string Description,
    int ContextWindow,
    bool SupportsVision,
    bool SupportsReasoning,
    bool SupportsToolCalling,
    int? SortOrder
) : ICommand<ErrorOr<LlmModelResult>>;