using System.Diagnostics;

using ErrorOr;

using Mediator;

using Microsoft.Extensions.Logging;

using Shared.Application.Messaging;

namespace Shared.Application.Behaviors;

public sealed partial class LoggingBehavior<TMessage, TResponse>(
    ILogger<LoggingBehavior<TMessage, TResponse>> logger)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMessage
{
    public async ValueTask<TResponse> Handle
    (
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken
    )
    {

        string requestName = typeof(TMessage).Name;

        LogHandling(requestName);

        if (logger.IsEnabled(LogLevel.Debug) && message is not ISensitiveRequest)
        {
            LogPayload(requestName, message);
        }

        long startingTimestamp = Stopwatch.GetTimestamp();

        try
        {
            TResponse response = await next(message, cancellationToken);
            long elapsedMs = (long)Stopwatch.GetElapsedTime(startingTimestamp).TotalMilliseconds;

            if (response is IErrorOr { IsError: true } errorOr)
            {
                LogHandledWithErrors(requestName, elapsedMs, errorOr.Errors ?? []);
            }
            else
            {
                LogHandled(requestName, elapsedMs);
            }

            return response;
        }
#pragma warning disable CA1031 // Behavior must observe and rethrow to preserve original stack while logging timing.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            long elapsedMs = (long)Stopwatch.GetElapsedTime(startingTimestamp).TotalMilliseconds;
            LogUnhandledException(ex, requestName, elapsedMs);
            throw;
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Handling {RequestName}")]
    private partial void LogHandling(string requestName);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "{RequestName} payload: {Request}")]
    private partial void LogPayload(string requestName, object request);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information,
        Message = "Handled {RequestName} in {ElapsedMilliseconds}ms")]
    private partial void LogHandled(string requestName, long elapsedMilliseconds);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning,
        Message = "Handled {RequestName} in {ElapsedMilliseconds}ms with errors: {Errors}")]
    private partial void LogHandledWithErrors(string requestName, long elapsedMilliseconds,
        IReadOnlyList<Error> errors);

    [LoggerMessage(EventId = 5, Level = LogLevel.Error,
        Message = "Unhandled exception while processing {RequestName} after {ElapsedMilliseconds}ms")]
    private partial void LogUnhandledException(Exception exception, string requestName, long elapsedMilliseconds);
}