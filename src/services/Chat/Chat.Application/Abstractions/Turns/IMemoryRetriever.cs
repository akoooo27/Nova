using Chat.Application.Turns;

namespace Chat.Application.Abstractions.Turns;

public interface IMemoryRetriever
{
    Task<RetrievedMemories> RetrieveAsync(TurnRequested job, CancellationToken cancellationToken);
}