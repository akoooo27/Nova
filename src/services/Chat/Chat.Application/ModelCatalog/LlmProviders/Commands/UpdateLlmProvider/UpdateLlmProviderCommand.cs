using Chat.Application.ModelCatalog.LlmProviders.Results;

using ErrorOr;

using Mediator;

namespace Chat.Application.ModelCatalog.LlmProviders.Commands.UpdateLlmProvider;

public sealed record UpdateLlmProviderCommand
(
    Guid ProviderId,
    string Name,
    string Slug,
    string? LogoKey,
    bool IsFeatured
) : ICommand<ErrorOr<LlmProviderResult>>;