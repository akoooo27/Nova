using ErrorOr;

using Mediator;

namespace Chat.Application.ModelCatalog.LlmProviders.Commands.DeleteLlmModel;

public sealed record DeleteLlmModelCommand
(
    Guid ProviderId,
    Guid ModelId
) : ICommand<ErrorOr<Success>>;