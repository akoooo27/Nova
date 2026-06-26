using Chat.Application.Abstractions.Turns;

using StackExchange.Redis;

namespace Chat.Infrastructure.Turns;

internal sealed class RedisTurnStopSignal(IConnectionMultiplexer redis) : ITurnStopSignal
{
    private const string KeyPrefix = "chat:turn-stop:";

    private static readonly TimeSpan StopSignalTtl = TimeSpan.FromMinutes(10);

    private static string StopKey(Guid assistantMessageId) => $"{KeyPrefix}{assistantMessageId}";

    public async Task RequestStopAsync(Guid assistantMessageId, CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        await redis
            .GetDatabase()
            .StringSetAsync
            (
                key: StopKey(assistantMessageId),
                value: "1",
                expiry: StopSignalTtl
            );
    }

    public async Task<bool> IsStopRequestedAsync(Guid assistantMessageId, CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        return await redis
            .GetDatabase()
            .KeyExistsAsync(StopKey(assistantMessageId));
    }
}