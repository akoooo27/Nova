using SharedKernel;

namespace Shared.Infrastructure.Time;

public sealed class DateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public DateTimeOffset Now => DateTimeOffset.Now;
}