using Chat.Application.Abstractions.Turns;
using Chat.Application.Turns;

namespace Chat.Application.Tests.Turns;

internal sealed class FakeMemoryRetriever : IMemoryRetriever
{
    public Task<RetrievedMemories> RetrieveAsync(TurnRequested job, CancellationToken cancellationToken) =>
        Task.FromResult(RetrievedMemories.Empty);
}