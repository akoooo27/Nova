using Chat.Domain.Chats;
using Chat.Domain.Shared;
using Chat.Infrastructure.Database;
using Chat.Infrastructure.Users.Models;

using ErrorOr;

using MassTransit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Shared.Contracts.IdentityIngress.Events;

namespace Chat.Infrastructure.Users.Consumers;

internal sealed partial class UserDeletedConsumer(
    ChatDbContext db,
    IChatRepository chats,
    ILogger<UserDeletedConsumer> logger)
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

        ErrorOr<UserId> userIdResult = UserId.Create(message.ProviderUserId);

        if (userIdResult.IsError)
        {
            LogChatPurgeSkippedForInvalidUserId(message.EventId, message.Provider, message.ProviderUserId);
        }
        else
        {
            int purged = await chats.DeleteAllAsync
            (
                userId: userIdResult.Value,
                includeTemporary: true,
                cancellationToken: context.CancellationToken
            );

            LogChatsPurgedForDeletedUser(purged, message.Provider, message.ProviderUserId);
        }

        await db.SaveChangesAsync(context.CancellationToken);

        LogUserDeletedProjected(message.EventId, message.Provider, message.ProviderUserId);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Projected deleted identity user {Provider}:{ProviderUserId} from event {EventId}")]
    private partial void LogUserDeletedProjected(string eventId, string provider, string providerUserId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information,
        Message = "Ignored stale deleted identity user event {EventId} for {Provider}:{ProviderUserId}")]
    private partial void LogStaleUserDeletedIgnored(string eventId, string provider, string providerUserId);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information,
        Message = "Purged {PurgedChatCount} chats for deleted identity user {Provider}:{ProviderUserId}")]
    private partial void LogChatsPurgedForDeletedUser(int purgedChatCount, string provider, string providerUserId);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning,
        Message = "Skipped chat purge for event {EventId}: invalid user id {Provider}:{ProviderUserId}")]
    private partial void LogChatPurgeSkippedForInvalidUserId(string eventId, string provider, string providerUserId);
}
