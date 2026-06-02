using Chat.Application.ModelCatalog.LlmProviders.Results;

using ErrorOr;

using Mediator;

namespace Chat.Application.ModelCatalog.LlmProviders.Commands.UpdateLlmModel;

public sealed record UpdateLlmModelCommand
(
    Guid ProviderId,
    Guid ModelId,
    string Name,
    string Description,
    int ContextWindow,
    bool SupportsVision,
    bool SupportsReasoning,
    bool SupportsToolCalling
) : ICommand<ErrorOr<LlmModelResult>>;