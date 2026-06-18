using FluentValidation;

namespace Chat.Application.ModelCatalog.ProviderLogos.Commands.RequestProviderLogoUploadUrl;

internal sealed class RequestProviderLogoUploadUrlCommandValidator
    : AbstractValidator<RequestProviderLogoUploadUrlCommand>
{
    public RequestProviderLogoUploadUrlCommandValidator()
    {
        RuleFor(x => x.ProviderId)
            .NotEmpty();

        RuleFor(x => x.ContentType)
            .NotEmpty()
            .MaximumLength(128);
    }
}