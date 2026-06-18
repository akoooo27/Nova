namespace Chat.Application.Abstractions.Analytics;

public interface IAnalytics
{
    void Capture
    (
        string distinctId,
        string eventName,
        Dictionary<string, object> properties
    );
}