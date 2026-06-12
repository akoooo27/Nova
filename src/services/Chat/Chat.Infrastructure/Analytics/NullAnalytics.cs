using Chat.Application.Abstractions.Analytics;

namespace Chat.Infrastructure.Analytics;

internal sealed class NullAnalytics : IAnalytics
{
    public void Capture(string distinctId, string eventName, Dictionary<string, object> properties)
    {
        // Analytics is optional; no configured PostHog key means no-op capture.
    }
}