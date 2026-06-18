using Chat.Domain.Chats;

using SharedKernel;

namespace Chat.Application.Chats.Cleanup;

public sealed class TemporaryChatCleaner(IChatRepository chats, IDateTimeProvider dateTimeProvider)
    : ITemporaryChatCleaner
{
    public Task<int> DeleteExpiredAsync(TimeSpan retentionPeriod, CancellationToken cancellationToken = default)
    {
        DateTimeOffset cutoff = dateTimeProvider.UtcNow - retentionPeriod;

        return chats.DeleteExpiredTemporaryChatsAsync(cutoff, cancellationToken);
    }
}