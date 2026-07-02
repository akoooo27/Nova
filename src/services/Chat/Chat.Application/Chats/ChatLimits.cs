namespace Chat.Application.Chats;

public static class ChatLimits
{
    public const int DefaultQueryLimit = 20;

    public const int DefaultQueryOffset = 0;

    public const int MinQueryLimit = 1;

    public const int MaxQueryLimit = 100;

    public const int MaxSearchQueryLength = 256;
}