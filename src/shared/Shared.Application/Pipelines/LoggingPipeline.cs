using System.Diagnostics;

using ErrorOr;

using MediatR;

using Microsoft.Extensions.Logging;

using Shared.Application.Messaging;

namespace Shared.Application.Pipelines;

public sealed class LoggingPipeline<TRequest, TResponse>(ILogger<LoggingPipeline<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        string requestName = typeof(TRequest).Name;

        if (logger.IsEnabled(LogLevel.Information))
        {
            if (request is ISensitiveRequest)
                logger.HandlingRequest(requestName);
            else
                logger.HandlingRequestWithPayload(requestName, request);
        }

        Stopwatch stopwatch = Stopwatch.StartNew();

        TResponse response = await next(cancellationToken);

        stopwatch.Stop();

        if (response is IErrorOr { IsError: true })
        {
            logger.HandledRequestWithFailure(requestName, stopwatch.ElapsedMilliseconds, response);
        }
        else if (logger.IsEnabled(LogLevel.Information))
        {
            if (request is ISensitiveRequest)
                logger.HandledRequestWithSuccess(requestName, stopwatch.ElapsedMilliseconds);
            else
                logger.HandledRequestWithSuccessAndPayload(requestName, stopwatch.ElapsedMilliseconds, response);
        }

        return response;
    }
}

internal static partial class LoggingPipelineLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Handling {RequestName}")]
    public static partial void HandlingRequest(this ILogger logger, string requestName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handling {RequestName} {@Request}")]
    public static partial void HandlingRequestWithPayload(this ILogger logger, string requestName, object request);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Handled {RequestName} in {ElapsedMilliseconds}ms with failure {@Response}")]
    public static partial void HandledRequestWithFailure(this ILogger logger, string requestName,
        long elapsedMilliseconds, object? response);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Handled {RequestName} in {ElapsedMilliseconds}ms with success")]
    public static partial void HandledRequestWithSuccess(this ILogger logger, string requestName,
        long elapsedMilliseconds);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Handled {RequestName} in {ElapsedMilliseconds}ms with success {@Response}")]
    public static partial void HandledRequestWithSuccessAndPayload(this ILogger logger, string requestName,
        long elapsedMilliseconds, object? response);
}