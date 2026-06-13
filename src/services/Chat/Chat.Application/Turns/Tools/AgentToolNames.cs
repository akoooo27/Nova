namespace Chat.Application.Turns.Tools;

public static class AgentToolNames
{
    public const string WebSearch = "web_search";

    private static readonly HashSet<string> Known = new(StringComparer.Ordinal) { WebSearch };

    public static bool IsKnown(string name) => Known.Contains(name);
}