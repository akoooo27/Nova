using Chat.Application.Abstractions.Turns;
using Chat.Application.Turns;

using StackExchange.Redis;

namespace Chat.Infrastructure.Turns;

internal sealed class RedisStreamTokenPublisher(IConnectionMultiplexer redis) : ITokenPublisher
{
    private const string KeyPrefix = "chat:turn:";

    private static readonly TimeSpan CompletedStreamTtl = TimeSpan.FromMinutes(10);

    internal static string StreamKey(Guid turnId) => $"{KeyPrefix}{turnId}";

    public async Task PublishAsync(TurnEvent turnEvent, CancellationToken cancellationToken)
    {
        IDatabase db = redis.GetDatabase();
        string key = StreamKey(turnEvent.TurnId);

        await db.StreamAddAsync
        (
            key: key,
            [new NameValueEntry("data", TurnEventSerializer.Serialize(turnEvent))]
        );

        if (turnEvent is DoneEvent or FailedEvent)
        {
            await db.KeyExpireAsync(key, CompletedStreamTtl);
        }
    }

    public async Task ResetAsync(Guid turnId, CancellationToken cancellationToken)
    {
        await redis
            .GetDatabase()
            .KeyDeleteAsync(StreamKey(turnId));
    }
}