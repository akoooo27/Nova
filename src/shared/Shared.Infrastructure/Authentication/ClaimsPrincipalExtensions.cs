using System.Security.Claims;

namespace Shared.Infrastructure.Authentication;

internal static class ClaimsPrincipalExtensions
{
    private const string Auth0SubjectClaim = "sub";

    public static string GetUserId(this ClaimsPrincipal? principal)
    {
        string? userId =
            principal?.FindFirstValue(Auth0SubjectClaim) ??
            principal?.FindFirstValue(ClaimTypes.NameIdentifier);

        return userId ?? throw new InvalidOperationException("User Id is unavailable.");
    }
}