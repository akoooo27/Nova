using Chat.Application.ModelCatalog.LlmProviders.Results;

using ErrorOr;

using Mediator;

namespace Chat.Application.ModelCatalog.LlmProviders.Commands.EnableLlmModel;

public sealed record EnableLlmModelCommand
(
    Guid ProviderId,
    Guid ModelId
) : ICommand<ErrorOr<LlmModelResult>>;