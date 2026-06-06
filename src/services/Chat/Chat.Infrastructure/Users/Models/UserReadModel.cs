using SharedKernel;

namespace Chat.Infrastructure.Users.Models;

internal sealed class UserReadModel
{
    public string ProviderUserId { get; private set; } = string.Empty;

    public string Provider { get; private set; } = string.Empty;

    public string? Email { get; private set; }

    public bool? EmailVerified { get; private set; }

    public string? Name { get; private set; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset LastObservedAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    private UserReadModel()
    {
        // For EF
    }

    private UserReadModel
    (
        string providerUserId,
        string provider,
        DateTimeOffset observedAt
    )
    {
        ProviderUserId = providerUserId;
        Provider = provider;
        LastObservedAt = observedAt;
        IsDeleted = false;
    }

    public static UserReadModel Create
    (
        string providerUserId,
        string provider,
        DateTimeOffset observedAt
    ) =>
        new
        (
            providerUserId: providerUserId,
            provider: provider,
            observedAt: observedAt
        );

    public bool IsStale(DateTimeOffset observedAt) =>
        observedAt < LastObservedAt;

    public void ApplyProfile
    (
        string? email,
        bool? emailVerified,
        string? name,
        DateTimeOffset observedAt
    )
    {
        Email = email;
        EmailVerified = emailVerified;
        Name = name;
        LastObservedAt = observedAt;
        IsDeleted = false;
        DeletedAt = null;
    }

    public void MarkDeleted(DateTimeOffset observedAt)
    {
        LastObservedAt = observedAt;
        IsDeleted = true;
        DeletedAt = observedAt;
    }
}