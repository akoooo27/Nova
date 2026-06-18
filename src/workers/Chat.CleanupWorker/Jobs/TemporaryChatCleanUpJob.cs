using Chat.Application.Chats.Cleanup;
using Chat.CleanupWorker.Options;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Chat.CleanupWorker.Jobs;

internal sealed partial class TemporaryChatCleanupJob(
    ITemporaryChatCleaner cleaner,
    IOptions<TemporaryChatCleanupOptions> options,
    ILogger<TemporaryChatCleanupJob> logger)
{
    private readonly TemporaryChatCleanupOptions _options = options.Value;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        TimeSpan retention = _options.RetentionPeriod;

        int deleted = await cleaner.DeleteExpiredAsync(retention, cancellationToken);

        LogDeleted(deleted, retention);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Temporary chat cleanup deleted {Count} chats (retention {Retention}).")]
    private partial void LogDeleted(int count, TimeSpan retention);
}