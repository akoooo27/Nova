using System.Text.Json;

using ArcadeDotnet;
using ArcadeDotnet.Models;
using ArcadeDotnet.Models.Admin.UserConnections;
using ArcadeDotnet.Models.Auth;

using Chat.Application.Abstractions.Arcade;
using Chat.Application.Abstractions.Arcade.Google;
using Chat.Infrastructure.Options;

using Microsoft.Extensions.Options;

using AuthRequirement = ArcadeDotnet.Models.Auth.AuthAuthorizeParamsProperties.AuthRequirement;
using AuthStatus = ArcadeDotnet.Models.AuthorizationResponseProperties.Status;
using ConnectionProvider = ArcadeDotnet.Models.Admin.UserConnections.UserConnectionListParamsProperties.Provider;
using ConnectionUser = ArcadeDotnet.Models.Admin.UserConnections.UserConnectionListParamsProperties.User;
using Oauth2 = ArcadeDotnet.Models.Auth.AuthAuthorizeParamsProperties.AuthRequirementProperties.Oauth2;

namespace Chat.Infrastructure.Arcade;

internal sealed class GoogleIntegrationClient(ArcadeClient client, IOptions<GoogleIntegrationOptions> options)
    : IGoogleIntegrationClient
{
    private const string Oauth2ProviderType = "oauth2";
    private const long ListPageSize = 100;

    private readonly GoogleIntegrationOptions _options = options.Value;

    public async Task<GoogleIntegrationStatus> GetStatusAsync(string userId, CancellationToken cancellationToken)
    {
        _ = cancellationToken; // Arcade SDK methods shown do not currently accept CT.

        IReadOnlyList<UserConnectionResponse> connections = await ListGoogleConnectionsAsync(userId);

        IReadOnlyList<string> scopes = connections
            .SelectMany(connection => connection.Scopes ?? [])
            .Distinct(StringComparer.Ordinal)
            .ToList();

        string? accountEmail = connections
            .Select(connection => TryGetAccountEmail(connection.ProviderUserInfo))
            .FirstOrDefault(email => email is not null);

        return new GoogleIntegrationStatus
        (
            Connected: connections.Count > 0,
            AccountEmail: accountEmail,
            Scopes: scopes
        );
    }

    public async Task<GoogleConnectResult> StartConnectAsync(string userId, CancellationToken cancellationToken)
    {
        _ = cancellationToken; // Arcade SDK methods shown do not currently accept CT.

        AuthorizationResponse response = await client.Auth.Authorize(new AuthAuthorizeParams
        {
            UserID = userId,

            // NextUri (the post-connect redirect) is intentionally omitted for now:
            // Arcade rejects a next_uri that isn't allow-listed for the provider.
            // Once PostConnectRedirectUri is registered in the Arcade dashboard,
            // send it here as NextUri = _options.PostConnectRedirectUri.ToString().
            AuthRequirement = new AuthRequirement
            {
                ProviderID = _options.ProviderId,
                ProviderType = Oauth2ProviderType,
                Oauth2 = new Oauth2 { Scopes = [.. _options.Scopes] }
            }
        });

        bool connected = response.Status?.Value() == AuthStatus.Completed;

        Uri? authorizationUrl = !connected && Uri.TryCreate(response.URL, UriKind.Absolute, out Uri? parsed)
            ? parsed
            : null;

        return new GoogleConnectResult(Connected: connected, AuthorizationUrl: authorizationUrl);
    }

    public async Task DisconnectAsync(string userId, CancellationToken cancellationToken)
    {
        _ = cancellationToken; // Arcade SDK methods shown do not currently accept CT.

        IReadOnlyList<UserConnectionResponse> connections = await ListGoogleConnectionsAsync(userId);

        // The DELETE /admin/user_connections/{id} path parameter is the
        // *Connection ID* (connection_id), not the record's ID (an "at_"-prefixed
        // value the endpoint rejects as "invalid prefixed ID format").
        IEnumerable<string> connectionIds = connections
            .Select(connection => connection.ConnectionID)
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => id!);

        foreach (string connectionId in connectionIds)
        {
            await client.Admin.UserConnections.Delete(new UserConnectionDeleteParams { ID = connectionId });
        }
    }

    private async Task<IReadOnlyList<UserConnectionResponse>> ListGoogleConnectionsAsync(string userId)
    {
        UserConnectionListPageResponse page = await client.Admin.UserConnections.List(new UserConnectionListParams
        {
            Provider = new ConnectionProvider { ID = _options.ProviderId },
            User = new ConnectionUser { ID = userId },
            Limit = ListPageSize
        });

        return page.Items ?? [];
    }

    private static string? TryGetAccountEmail(JsonElement? providerUserInfo)
    {
        if (providerUserInfo is not { ValueKind: JsonValueKind.Object } info)
        {
            return null;
        }

        return info.TryGetProperty("email", out JsonElement email) && email.ValueKind == JsonValueKind.String
            ? email.GetString()
            : null;
    }
}