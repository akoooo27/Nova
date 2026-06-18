using Chat.Application.ModelCatalog.LlmProviders.Results;

using ErrorOr;

using Mediator;

namespace Chat.Application.ModelCatalog.LlmProviders.Commands.EnableLlmProvider;

public sealed record EnableLlmProviderCommand(Guid ProviderId) : ICommand<ErrorOr<LlmProviderResult>>;