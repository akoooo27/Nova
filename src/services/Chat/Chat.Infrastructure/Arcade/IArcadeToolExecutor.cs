using System.Text.Json;

namespace Chat.Infrastructure.Arcade;

public interface IArcadeToolExecutor
{
    Task<JsonElement> ExecuteAsync
    (
        string toolName,
        string userId,
        IReadOnlyDictionary<string, JsonElement>? input,
        CancellationToken cancellationToken
    );
}