using Chat.Infrastructure.Database;
using Chat.Infrastructure.Users.Models;

using MassTransit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Shared.Contracts.IdentityIngress.Events;

using SharedKernel;

namespace Chat.Infrastructure.Users.Consumers;

internal sealed partial class UserRegisteredConsumer(ChatDbContext db, ILogger<UserRegisteredConsumer> logger)
    : IConsumer<UserRegistered>
{
    public async Task Consume(ConsumeContext<UserRegistered> context)
    {
        UserRegistered message = context.Message;

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
            LogStaleUserRegisteredIgnored(message.EventId, message.Provider, message.ProviderUserId);
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

        LogUserRegisteredProjected(message.EventId, message.Provider, message.ProviderUserId);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Projected registered identity user {Provider}:{ProviderUserId} from event {EventId}")]
    private partial void LogUserRegisteredProjected(string eventId, string provider, string providerUserId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information,
        Message = "Ignored stale registered identity user event {EventId} for {Provider}:{ProviderUserId}")]
    private partial void LogStaleUserRegisteredIgnored(string eventId, string provider, string providerUserId);
}