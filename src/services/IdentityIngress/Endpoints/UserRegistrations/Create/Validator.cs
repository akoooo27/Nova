using FastEndpoints;

using FluentValidation;

namespace IdentityIngress.Endpoints.UserRegistrations.Create;

internal sealed class Validator : Validator<Request>
{
    public Validator()
    {
        RuleFor(request => request.Sub)
            .NotEmpty();

        RuleFor(request => request.Email)
            .EmailAddress()
            .When(request => request.Email is not null);

        RuleFor(request => request.Name)
            .NotEmpty()
            .When(request => request.Name is not null);
    }
}