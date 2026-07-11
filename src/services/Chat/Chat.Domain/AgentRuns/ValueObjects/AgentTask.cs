using ErrorOr;

namespace Chat.Domain.AgentRuns.ValueObjects;

public sealed record AgentTask
{
    public const int MaxLength = 32_768;

    public string Value { get; }

    private AgentTask(string value)
    {
        Value = value;
    }

    public static ErrorOr<AgentTask> Create(string? value)
    {
        string trimmed = value?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return Error.Validation
            (
                code: "AgentTask.Required",
                description: "Agent task is required."
            );
        }

        if (trimmed.Length > MaxLength)
        {
            return Error.Validation
            (
                code: "AgentTask.TooLong",
                description: $"Agent task cannot exceed {MaxLength} characters."
            );
        }

        return new AgentTask(trimmed);
    }

    public static AgentTask FromDatabase(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Length > MaxLength)
            throw new DomainException("Database contained an invalid agent task.");

        return new AgentTask(value);
    }

    public override string ToString() => Value;
}