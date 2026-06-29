using Chat.Domain.Personalizations;
using Chat.Domain.Shared;

namespace Chat.Application.Tests.Turns;

internal sealed class FakePersonalizationRepository : IPersonalizationRepository
{
    private readonly List<Personalization> _personalizations = [];

    public void AddExisting(Personalization personalization) => _personalizations.Add(personalization);

    public Task<Personalization?> GetByUserIdAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        Personalization? personalization = _personalizations.FirstOrDefault(p => p.UserId == userId);

        return Task.FromResult(personalization);
    }

    public void Add(Personalization personalization) => _personalizations.Add(personalization);
}