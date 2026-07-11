using System.Text.Json;

using ErrorOr;

namespace Chat.Domain.AgentRuns.ValueObjects;

public sealed record ActivityDetail
{
    public const int MaxLength = 16_384;

    public string Value { get; }

    private ActivityDetail(string value)
    {
        Value = value;
    }

    public static ErrorOr<ActivityDetail> Create(string? value)
    {
        string trimmed = value?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return Error.Validation
            (
                code: "ActivityDetail.Required",
                description: "Activity detail is required."
            );
        }

        if (trimmed.Length > MaxLength)
        {
            return Error.Validation
            (
                code: "ActivityDetail.TooLong",
                description: $"Activity detail cannot exceed {MaxLength} characters."
            );
        }

        if (!IsValidJson(trimmed))
        {
            return Error.Validation
            (
                code: "ActivityDetail.InvalidJson",
                description: "Activity detail must be valid JSON."
            );
        }

        return new ActivityDetail(trimmed);
    }

    public static ActivityDetail FromDatabase(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Length > MaxLength || !IsValidJson(value))
            throw new DomainException("Database contained an invalid activity detail.");

        return new ActivityDetail(value);
    }

    public override string ToString() => Value;

    private static bool IsValidJson(string value)
    {
        try
        {
            using JsonDocument _ = JsonDocument.Parse(value);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}