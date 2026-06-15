namespace Chat.Application.Chats.Cleanup;

public interface ITemporaryChatCleaner
{
    Task<int> DeleteExpiredAsync(TimeSpan retentionPeriod, CancellationToken cancellationToken = default);
}