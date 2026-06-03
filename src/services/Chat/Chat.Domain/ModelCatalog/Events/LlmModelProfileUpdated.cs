using Chat.Domain.ModelCatalog.ValueObjects;

using SharedKernel;

namespace Chat.Domain.ModelCatalog.Events;

public sealed record LlmModelProfileUpdated
(
    LlmProviderId ProviderId,
    LlmModelId ModelId
) : IDomainEvent;