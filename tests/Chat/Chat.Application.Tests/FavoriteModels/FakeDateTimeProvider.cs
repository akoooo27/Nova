using SharedKernel;

namespace Chat.Application.Tests.FavoriteModels;

internal sealed class FakeDateTimeProvider(DateTimeOffset utcNow) : IDateTimeProvider
{
    public DateTimeOffset UtcNow { get; } = utcNow;

    public DateTimeOffset Now => UtcNow.ToLocalTime();
}