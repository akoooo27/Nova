namespace Chat.Application.Abstractions.Gmail;

public interface IGmailToolClient
{
    Task<GmailToolResult> WhoAmIAsync(string userId, CancellationToken cancellationToken);
}