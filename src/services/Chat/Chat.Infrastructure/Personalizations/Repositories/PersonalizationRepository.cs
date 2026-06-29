using Chat.Domain.Personalizations;
using Chat.Domain.Shared;
using Chat.Infrastructure.Database;

using Microsoft.EntityFrameworkCore;

namespace Chat.Infrastructure.Personalizations.Repositories;

internal sealed class PersonalizationRepository(ChatDbContext db) : IPersonalizationRepository
{
    public async Task<Personalization?> GetByUserIdAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        return await db.Personalizations
            .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
    }

    public void Add(Personalization personalization)
    {
        db.Personalizations.Add(personalization);
    }
}