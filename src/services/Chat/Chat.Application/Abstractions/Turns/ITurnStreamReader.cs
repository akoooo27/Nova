using Chat.Application.Turns;

namespace Chat.Application.Abstractions.Turns;

public sealed record TurnStreamEntry(string EntryId, TurnEvent Event);

public interface ITurnStreamReader
{
    /// <summary>
    /// Reads turn events from the per-turn stream starting after <paramref name="fromEntryId"/>
    /// (or from the beginning when null), completing after a done/failed event.
    /// </summary>
    IAsyncEnumerable<TurnStreamEntry> ReadAsync(Guid turnId, string? fromEntryId, CancellationToken cancellationToken);
}