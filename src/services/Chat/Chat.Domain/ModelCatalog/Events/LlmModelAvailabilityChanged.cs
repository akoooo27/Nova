using Chat.Domain.ModelCatalog.ValueObjects;

using SharedKernel;

namespace Chat.Domain.ModelCatalog.Events;

public sealed record LlmModelAvailabilityChanged
(
    LlmProviderId ProviderId,
    LlmModelId ModelId,
    bool IsEnabled
) : IDomainEvent;