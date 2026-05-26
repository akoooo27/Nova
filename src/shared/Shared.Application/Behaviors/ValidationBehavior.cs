using ErrorOr;

using FluentValidation;
using FluentValidation.Results;

using Mediator;

namespace Shared.Application.Behaviors;

public class ValidationBehavior<TRequest, TResponse>(IValidator<TRequest>? validator = null)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : IErrorOr
{
    public async ValueTask<TResponse> Handle
    (
        TRequest message,
        MessageHandlerDelegate<TRequest, TResponse> next,
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