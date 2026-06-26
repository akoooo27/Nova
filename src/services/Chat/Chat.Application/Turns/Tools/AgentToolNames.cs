namespace Chat.Application.Turns.Tools;

public static class AgentToolNames
{
    public const string WebSearch = "web_search";

    public const string ReadUrl = "read_url";

    public const string GmailWhoAmI = "gmail_whoami";

    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        WebSearch,
        ReadUrl,
        GmailWhoAmI
    };

    public static bool IsKnown(string name) => Known.Contains(name);
}