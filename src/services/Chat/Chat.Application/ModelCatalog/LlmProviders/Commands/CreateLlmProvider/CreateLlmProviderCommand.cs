using Chat.Application.ModelCatalog.LlmProviders.Results;

using ErrorOr;

using Mediator;

namespace Chat.Application.ModelCatalog.LlmProviders.Commands.CreateLlmProvider;

public sealed record CreateLlmProviderCommand
(
    string Name,
    string Slug,
    bool IsFeatured,
    string? LogoKey
) : ICommand<ErrorOr<LlmProviderResult>>;