using System.Text.Json;

using ArcadeDotnet;
using ArcadeDotnet.Models.Tools;

namespace Chat.Infrastructure.Arcade;

internal sealed class ArcadeToolExecutor(ArcadeClient client) : IArcadeToolExecutor
{
    public async Task<JsonElement> ExecuteAsync
    (
        string toolName,
        string userId,
        IReadOnlyDictionary<string, JsonElement>? input,
        CancellationToken cancellationToken
    )
    {
        _ = cancellationToken; // Arcade SDK methods shown do not currently accept CT.

        // Remove this code, when real auth is added
        await client.Tools.Authorize(new ToolAuthorizeParams
        {
            ToolName = toolName,
            UserID = userId
        });

        ExecuteToolResponse response = await client.Tools.Execute(new ToolExecuteParams
        {
            ToolName = toolName,
            UserID = userId,
            Input = input is null ? null : new Dictionary<string, JsonElement>(input)
        });

        if (response.Output?.Authorization?.URL is not null)
        {
            return JsonSerializer.SerializeToElement(new
            {
                status = "authorization_required",
                authorization_url = response.Output.Authorization.URL,
                authorization_id = response.Output.Authorization.ID
            });
        }

        // Arcade reports Success == true once the execution *request* completes, even when
        // the tool itself failed upstream (e.g. a disabled Google API). The real tool error
        // lives in Output.Error, so surface that as a failure rather than letting it fall
        // through to the "empty" branch and masquerade as "no data".
        if (response.Success == false || response.Output?.Error is not null)
        {
            return JsonSerializer.SerializeToElement(new
            {
                status = "failed",
                error = response.Output?.Error
            });
        }

        return response.Output?.Value
               ?? JsonSerializer.SerializeToElement(new { status = "empty" });
    }
}
