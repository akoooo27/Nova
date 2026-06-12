using Chat.Application.Turns;

namespace Chat.Application.Abstractions.Turns;

public interface ITokenPublisher
{
    Task PublishAsync(TurnEvent turnEvent, CancellationToken cancellationToken);

    /// <summary>Deletes any partial stream left behind by a crashed previous attempt.</summary>
    Task ResetAsync(Guid turnId, CancellationToken cancellationToken);
}