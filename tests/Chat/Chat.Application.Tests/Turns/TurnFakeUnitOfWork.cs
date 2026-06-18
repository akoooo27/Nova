using Chat.Application.Abstractions.Database;

namespace Chat.Application.Tests.Turns;

internal sealed class TurnFakeUnitOfWork : IUnitOfWork
{
    public int SaveCount { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;

        return Task.FromResult(1);
    }
}