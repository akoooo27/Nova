using System.Security.Claims;

namespace Shared.Infrastructure.Authentication;

internal static class ClaimsPrincipalExtensions
{
    public static string GetUserId(this ClaimsPrincipal? principal)
    {
        string? userId = principal?.FindFirstValue(ClaimTypes.NameIdentifier);

        return userId ?? throw new InvalidOperationException("User Id is unavailable.");
    }
}