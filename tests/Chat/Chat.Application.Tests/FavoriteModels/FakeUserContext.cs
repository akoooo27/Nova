using Shared.Application.Authentication;

namespace Chat.Application.Tests.FavoriteModels;

internal sealed class FakeUserContext(string userId) : IUserContext
{
    public string UserId { get; } = userId;
}