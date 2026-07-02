using ErrorOr;

using FluentValidation;
using FluentValidation.Results;

using Mediator;

namespace Shared.Application.Behaviors;

public class ValidationBehavior<TMessage, TResponse>(IValidator<TMessage>? validator = null)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMessage
    where TResponse : IErrorOr
{
    public async ValueTask<TResponse> Handle
    (
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken
    )
    {
        if (validator is null)
        {
            return await next(message, cancellationToken);
        }

        ValidationResult? validationResult = await validator.ValidateAsync(message, cancellationToken);

        if (validationResult.IsValid)
        {
            return await next(message, cancellationToken);
        }

        List<Error> errors = validationResult.Errors
            .ConvertAll(error => Error.Validation
                (
                    code: error.PropertyName,
                    description: error.ErrorMessage)
            );

        return (dynamic)errors;
    }
}
