using ErrorOr;

using FluentValidation;
using FluentValidation.Results;

using MediatR;

namespace Shared.Application.Pipelines;

public sealed class ValidationBehavior<TRequest, TResponse>(IValidator<TRequest>? validator = null)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : IErrorOr
{
    private readonly IValidator<TRequest>? _validator = validator;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (_validator is null)
        {
            return await next(cancellationToken);
        }

        ValidationResult? validationResult = await _validator.ValidateAsync(request, cancellationToken);

        if (validationResult.IsValid)
        {
            return await next(cancellationToken);
        }

        List<Error> errors = validationResult.Errors
            .ConvertAll(error => Error.Validation
                (
                    code: error.PropertyName,
                    description: error.ErrorMessage
                )
            );

        return (dynamic)errors;
    }
}