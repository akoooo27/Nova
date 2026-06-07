using ErrorOr;

using Mediator;

namespace Chat.Application.ModelCatalog.LlmProviders.Commands.DeleteLlmProvider;

public sealed record DeleteLlmProviderCommand(Guid Id) : ICommand<ErrorOr<Success>>;