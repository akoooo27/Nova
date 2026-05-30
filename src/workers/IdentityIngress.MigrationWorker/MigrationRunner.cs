using IdentityIngress.Database;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IdentityIngress.MigrationWorker;

internal sealed partial class MigrationRunner(
    IdentityIngressDbContext dbContext,
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

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Starting IdentityIngress database migration")]
    private static partial void LogStarting(ILogger logger);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Completed IdentityIngress database migration")]
    private static partial void LogCompleted(ILogger logger);

    [LoggerMessage(EventId = 3, Level = LogLevel.Error, Message = "Failed to migrate IdentityIngress database")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}