using ErrorOr;

namespace IdentityIngress.IdentityProviders;

internal interface IIdentityProviderEventMapper<in TRequest>
{
    ErrorOr<MappedIdentityEvent> Map(TRequest request);
}