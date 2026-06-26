using System.Text.Json;

using Chat.Application.Abstractions.Gmail;
using Chat.Infrastructure.Arcade;

namespace Chat.Infrastructure.Gmail;

internal sealed class ArcadeGmailToolClient(IArcadeToolExecutor arcade) : IGmailToolClient
{
    private const string WhoAmIToolName = "Gmail.WhoAmI";

    public async Task<GmailToolResult> WhoAmIAsync
    (
        string userId,
        CancellationToken cancellationToken
    )
    {
        JsonElement data = await arcade.ExecuteAsync
        (
            toolName: WhoAmIToolName,
            userId: userId,
            input: null,
            cancellationToken: cancellationToken
        );

        string? status = TryGetStatus(data);

        return new GmailToolResult
        (
            Available: status is null,
            Data: data,
            Note: status switch
            {
                "authorization_required" => "Gmail authorization is required before this tool can be used.",
                "failed" => "Gmail is currently unavailable.",
                "empty" => "Gmail returned no profile data.",
                _ => null
            }
        );
    }

    private static string? TryGetStatus(JsonElement data)
    {
        return data.ValueKind == JsonValueKind.Object
               && data.TryGetProperty("status", out JsonElement status)
               && status.ValueKind == JsonValueKind.String
            ? status.GetString()
            : null;
    }
}