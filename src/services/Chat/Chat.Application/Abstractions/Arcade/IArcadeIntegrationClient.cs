namespace Chat.Application.Abstractions.Arcade;

public interface IArcadeIntegrationClient<TStatus, TConnectResult>
{
    Task<TStatus> GetStatusAsync(string userId, CancellationToken cancellationToken);

    Task<TConnectResult> StartConnectAsync(string userId, CancellationToken cancellationToken);

    Task DisconnectAsync(string userId, CancellationToken cancellationToken);
}