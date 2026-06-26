using System.Text.Json;

namespace Chat.Application.Abstractions.Gmail;

public sealed record GmailToolResult
(
    bool Available,
    JsonElement? Data,
    string? Note
);