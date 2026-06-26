using ArcadeDotnet;
using ArcadeDotnet.Models.Auth;

using Chat.Application.Abstractions.Arcade;

namespace Chat.Infrastructure.Arcade;

internal sealed class ArcadeAuthClient(ArcadeClient client) : IArcadeAuthClient
{
    public async Task<ArcadeUserConfirmation> ConfirmUserAsync
    (
        string flowId,
        string userId,
        CancellationToken cancellationToken
    )
    {
        _ = cancellationToken; // Arcade SDK methods shown do not currently accept CT.

        ConfirmUserResponse response = await client.Auth.ConfirmUser(new AuthConfirmUserParams
        {
            FlowID = flowId,
            UserID = userId
        });

        Uri? nextUri = Uri.TryCreate(response.NextUri, UriKind.Absolute, out Uri? parsed)
            ? parsed
            : null;

        return new ArcadeUserConfirmation(NextUri: nextUri);
    }
}