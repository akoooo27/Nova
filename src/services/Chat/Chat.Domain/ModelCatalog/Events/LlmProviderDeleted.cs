using Chat.Domain.ModelCatalog.ValueObjects;

using SharedKernel;

namespace Chat.Domain.ModelCatalog.Events;

public sealed record LlmProviderDeleted(LlmProviderId ProviderId) : IDomainEvent;