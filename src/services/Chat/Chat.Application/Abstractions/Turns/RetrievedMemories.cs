namespace Chat.Application.Abstractions.Turns;

public sealed record RetrievedMemories(IReadOnlyList<string> Items)
{
    public static readonly RetrievedMemories Empty = new([]);
}