namespace Chat.Application.Abstractions.Arcade;

public interface IArcadeAuthClient
{
    Task<ArcadeUserConfirmation> ConfirmUserAsync(string flowId, string userId, CancellationToken cancellationToken);
}