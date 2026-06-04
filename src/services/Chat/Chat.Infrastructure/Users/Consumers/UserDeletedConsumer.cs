using Chat.Infrastructure.Database;
using Chat.Infrastructure.Users.Models;

using MassTransit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Shared.Contracts.IdentityIngress.Events;

namespace Chat.Infrastructure.Users.Consumers;

internal sealed partial class UserDeletedConsumer(ChatDbContext db, ILogger<UserDeletedConsumer> logger)
    : IConsumer<UserDeleted>
{
    public async Task Consume(ConsumeContext<UserDeleted> context)
    {
        UserDeleted message = context.Message;

        UserReadModel? user = await db.Users.SingleOrDefaultAsync
        (
            candidate => candidate.ProviderUserId == message.ProviderUserId &&
                         candidate.Provider == message.Provider,
            context.CancellationToken
        );

        if (user is null)
        {
            user = UserReadModel.Create
            (
                providerUserId: message.ProviderUserId,
                provider: message.Provider,
                observedAt: message.OccurredAt
            );

            db.Users.Add(user);
        }

        if (user.IsStale(message.OccurredAt))
        {
            LogStaleUserDeletedIgnored(message.EventId, message.Provider, message.ProviderUserId);
            return;
        }

        user.MarkDeleted(message.OccurredAt);

        await db.SaveChangesAsync(context.CancellationToken);

        LogUserDeletedProjected(message.EventId, message.Provider, message.ProviderUserId);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Projected deleted identity user {Provider}:{ProviderUserId} from event {EventId}")]
    private partial void LogUserDeletedProjected(string eventId, string provider, string providerUserId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information,
        Message = "Ignored stale deleted identity user event {EventId} for {Provider}:{ProviderUserId}")]
    private partial void LogStaleUserDeletedIgnored(string eventId, string provider, string providerUserId);
}