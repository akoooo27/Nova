using Chat.Application.Abstractions.Turns;

namespace Chat.Application.Turns;

public sealed class NoOpMemoryRetriever : IMemoryRetriever
{
    public Task<RetrievedMemories> RetrieveAsync(TurnRequested job, CancellationToken cancellationToken) =>
        Task.FromResult(RetrievedMemories.Empty);
}