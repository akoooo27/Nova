using System.Runtime.CompilerServices;

using Chat.Application.Abstractions.Turns;
using Chat.Application.Turns;

using StackExchange.Redis;

namespace Chat.Infrastructure.Turns;

internal sealed class RedisTurnStreamReader(IConnectionMultiplexer redis) : ITurnStreamReader
{
    private static readonly TimeSpan PollDelay = TimeSpan.FromMilliseconds(150);

    public async IAsyncEnumerable<TurnStreamEntry> ReadAsync
    (
        Guid turnId,
        string? fromEntryId,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        IDatabase db = redis.GetDatabase();
        string key = RedisStreamTokenPublisher.StreamKey(turnId);
        RedisValue position = fromEntryId ?? "0-0";

        while (!cancellationToken.IsCancellationRequested)
        {
            StreamEntry[] entries = await db.StreamReadAsync
            (
                key: key,
                position: position,
                count: 128
            );

            foreach (StreamEntry entry in entries)
            {
                position = entry.Id;

                string? json = entry["data"];

                if (json is null)
                {
                    continue;
                }

                TurnEvent? turnEvent = TurnEventSerializer.Deserialize(json);

                if (turnEvent is null)
                {
                    continue;
                }

                TurnStreamEntry turnStreamEntry = new(entry.Id.ToString(), turnEvent);

                yield return turnStreamEntry;

                if (turnEvent is DoneEvent or FailedEvent)
                {
                    yield break;
                }
            }

            if (entries.Length == 0)
            {
                await Task.Delay(PollDelay, cancellationToken);
            }
        }
    }
}