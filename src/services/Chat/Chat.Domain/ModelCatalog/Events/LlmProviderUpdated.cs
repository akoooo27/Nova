using Chat.Domain.ModelCatalog.ValueObjects;

using SharedKernel;

namespace Chat.Domain.ModelCatalog.Events;

public sealed record LlmProviderUpdated(LlmProviderId ProviderId) : IDomainEvent;