using Chat.Infrastructure.Database;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Chat.MigrationWorker;

internal sealed partial class MigrationRunner(
    ChatDbContext dbContext,
    ILogger<MigrationRunner> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            LogStarting(logger);
            await dbContext.Database.MigrateAsync(cancellationToken);
            LogCompleted(logger);
        }
        catch (Exception exception)
        {
            LogFailed(logger, exception);
            throw;
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Starting Chat database migration")]
    private static partial void LogStarting(ILogger logger);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Completed Chat database migration")]
    private static partial void LogCompleted(ILogger logger);

    [LoggerMessage(EventId = 3, Level = LogLevel.Error, Message = "Failed to migrate Chat database")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}