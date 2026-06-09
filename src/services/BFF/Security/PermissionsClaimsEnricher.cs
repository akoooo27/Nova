using Duende.Bff;
using Duende.Bff.Endpoints;

using Microsoft.AspNetCore.Authentication;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BFF.Security;

/// <summary>
/// Projects the user's <c>permissions</c> claims from the access token into the
/// <c>/bff/user</c> response so the SPA can decide which privileged UI to render.
/// Authorization itself is still enforced by the downstream APIs.
/// </summary>
internal sealed class PermissionsClaimsEnricher : IUserEndpointClaimsEnricher
{
    private const string PermissionsClaimType = "permissions";

    public Task<IReadOnlyList<ClaimRecord>> EnrichClaimsAsync(
        AuthenticateResult authenticateResult,
        IReadOnlyList<ClaimRecord> claims,
        CancellationToken ct = default)
    {
        string? accessToken = authenticateResult.Properties?.GetTokenValue("access_token");

        if (string.IsNullOrEmpty(accessToken))
        {
            return Task.FromResult(claims);
        }

        JsonWebToken token = new(accessToken);

        IReadOnlyList<ClaimRecord> enriched =
        [
            .. claims,
            .. token.Claims
                .Where(claim => claim.Type == PermissionsClaimType)
                .Select(claim => new ClaimRecord(PermissionsClaimType, claim.Value))
        ];

        return Task.FromResult(enriched);
    }
}
