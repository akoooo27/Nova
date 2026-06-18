using Chat.Application.ModelCatalog.LlmProviders.Results;

using ErrorOr;

using Mediator;

namespace Chat.Application.ModelCatalog.LlmProviders.Commands.DisableLlmProvider;

public sealed record DisableLlmProviderCommand(Guid ProviderId) : ICommand<ErrorOr<LlmProviderResult>>;