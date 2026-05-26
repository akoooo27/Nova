using Microsoft.AspNetCore.Http;

using Shared.Application.Authentication;

namespace Shared.Infrastructure.Authentication;

internal sealed class UserContext(IHttpContextAccessor httpContextAccessor) : IUserContext
{
    public string UserId =>
        httpContextAccessor
            .HttpContext?
            .User
            .GetUserId() ??
        throw new InvalidOperationException("User context is unavailable");
}