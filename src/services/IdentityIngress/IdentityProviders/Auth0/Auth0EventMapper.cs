using ErrorOr;

using IdentityIngress.Endpoints.Auth0Events;

using Shared.Contracts.IdentityIngress.Events;

namespace IdentityIngress.IdentityProviders.Auth0;

internal sealed class Auth0EventMapper : IIdentityProviderEventMapper<Request>
{
    private const string Provider = "auth0";
    private const string UserCreated = "user.created";
    private const string UserUpdated = "user.updated";
    private const string UserDeleted = "user.deleted";

    public ErrorOr<MappedIdentityEvent> Map(Request providerEvent)
    {
        if (providerEvent.Type is not (UserCreated or UserUpdated or UserDeleted))
        {
            return Auth0Errors.UnsupportedEventType(providerEvent.Type);
        }

        Auth0UserObject? user = providerEvent.Data?.Object;

        if (user?.UserId is not { Length: > 0 } providerUserId)
        {
            return Auth0Errors.InvalidPayload;
        }

        object integrationEvent = providerEvent.Type switch
        {
            UserCreated => new UserRegistered
            {
                EventId = providerEvent.Id,
                Provider = Provider,
                ProviderUserId = providerUserId,
                OccurredAt = providerEvent.Time,
                Email = user.Email,
                EmailVerified = user.EmailVerified,
                Name = user.Name
            },
            UserUpdated => new UserUpdated
            {
                EventId = providerEvent.Id,
                Provider = Provider,
                ProviderUserId = providerUserId,
                OccurredAt = providerEvent.Time,
                Email = user.Email,
                EmailVerified = user.EmailVerified,
                Name = user.Name
            },
            UserDeleted => new UserDeleted
            {
                EventId = providerEvent.Id,
                Provider = Provider,
                ProviderUserId = providerUserId,
                OccurredAt = providerEvent.Time
            },
            _ => Auth0Errors.UnsupportedEventTypeMapped(providerEvent.Type)
        };

        return new MappedIdentityEvent(integrationEvent, providerEvent.Type);
    }
}