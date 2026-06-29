using Chat.Domain.Shared;

namespace Chat.Domain.Personalizations;

public interface IPersonalizationRepository
{
    Task<Personalization?> GetByUserIdAsync(UserId userId, CancellationToken cancellationToken = default);

    void Add(Personalization personalization);
}