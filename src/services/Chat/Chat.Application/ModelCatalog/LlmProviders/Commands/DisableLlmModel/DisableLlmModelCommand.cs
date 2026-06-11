using Chat.Application.ModelCatalog.LlmProviders.Results;

using ErrorOr;

using Mediator;

namespace Chat.Application.ModelCatalog.LlmProviders.Commands.DisableLlmModel;

public sealed record DisableLlmModelCommand
(
    Guid ProviderId,
    Guid ModelId
) : ICommand<ErrorOr<LlmModelResult>>;