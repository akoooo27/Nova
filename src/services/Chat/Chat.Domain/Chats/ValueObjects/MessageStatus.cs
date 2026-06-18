namespace Chat.Domain.Chats.ValueObjects;

#pragma warning disable CA1008
public enum MessageStatus
{
    Generating = 1,
    Completed = 2,
    Failed = 3
}