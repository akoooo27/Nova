using Chat.Infrastructure.Database;
using Chat.Infrastructure.Users.Models;

using MassTransit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Shared.Contracts.IdentityIngress.Events;

using SharedKernel;

namespace Chat.Infrastructure.Users.Consumers;

internal sealed partial class UserUpdatedConsumer(
    ChatDbContext db, ILogger<UserUpdatedConsumer> logger)
    : IConsumer<UserUpdated>
{
    public async Task Consume(ConsumeContext<UserUpdated> context)
    {
        UserUpdated message = context.Message;

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
            LogStaleUserUpdatedIgnored(message.EventId, message.Provider, message.ProviderUserId);
            return;
        }

        user.ApplyProfile
        (
            email: message.Email,
            emailVerified: message.EmailVerified,
            name: message.Name,
            observedAt: message.OccurredAt
        );

        await db.SaveChangesAsync(context.CancellationToken);

        LogUserUpdatedProjected(message.EventId, message.Provider, message.ProviderUserId);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Projected updated identity user {Provider}:{ProviderUserId} from event {EventId}")]
    private partial void LogUserUpdatedProjected(string eventId, string provider, string providerUserId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information,
        Message = "Ignored stale updated identity user event {EventId} for {Provider}:{ProviderUserId}")]
    private partial void LogStaleUserUpdatedIgnored(string eventId, string provider, string providerUserId);
}