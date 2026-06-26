using System.Text.Json.Serialization;

namespace Chat.Application.Turns;

/// <summary>
/// The streaming vocabulary of a chat turn. APPEND-ONLY (spec Rule 6):
/// new derived events may be added; existing shapes and discriminators must never change.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TokenEvent), "token")]
[JsonDerivedType(typeof(ToolCallEvent), "tool_call")]
[JsonDerivedType(typeof(ToolResultEvent), "tool_result")]
[JsonDerivedType(typeof(UsageEvent), "usage")]
[JsonDerivedType(typeof(DoneEvent), "done")]
[JsonDerivedType(typeof(FailedEvent), "failed")]
[JsonDerivedType(typeof(ReasoningEvent), "reasoning")]
[JsonDerivedType(typeof(StoppedEvent), "stopped")]
public abstract record TurnEvent(Guid TurnId);

public sealed record TokenEvent(Guid TurnId, string Text) : TurnEvent(TurnId);

public sealed record ToolCallEvent(
    Guid TurnId,
    string Tool,
    string ArgsJson
) : TurnEvent(TurnId);

public sealed record ToolResultEvent(
    Guid TurnId,
    string Tool,
    string Summary
) : TurnEvent(TurnId);

public sealed record UsageEvent(
    Guid TurnId,
    string Model,
    int InputTokens,
    int OutputTokens
) : TurnEvent(TurnId);

public sealed record DoneEvent(Guid TurnId) : TurnEvent(TurnId);

public sealed record FailedEvent(Guid TurnId, string Reason) : TurnEvent(TurnId);

public sealed record ReasoningEvent(Guid TurnId, string Text) : TurnEvent(TurnId);

public sealed record StoppedEvent(Guid TurnId) : TurnEvent(TurnId);