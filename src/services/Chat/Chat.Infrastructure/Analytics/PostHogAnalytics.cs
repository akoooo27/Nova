using Chat.Application.Abstractions.Analytics;

using PostHog;

namespace Chat.Infrastructure.Analytics;

internal sealed class PostHogAnalytics(IPostHogClient client) : IAnalytics
{
    public void Capture
    (
        string distinctId,
        string eventName,
        Dictionary<string, object> properties
    ) => client.Capture
    (
        distinctId: distinctId,
        eventName,
        properties
    );
}