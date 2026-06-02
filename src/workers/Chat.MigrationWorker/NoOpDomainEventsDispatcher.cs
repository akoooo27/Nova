using Shared.Infrastructure.DomainEvents;

using SharedKernel;

namespace Chat.MigrationWorker;

internal sealed class NoOpDomainEventsDispatcher : IDomainEventsDispatcher
{
    public Task DispatchAsync(
        IEnumerable<IDomainEvent> domainEvents,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}