using ErrorOr;

namespace Chat.Domain.AgentRuns.ValueObjects;

public sealed record TokenUsage
{
    public int InputTokens { get; private init; }

    public int OutputTokens { get; private init; }

    private TokenUsage()
    {
        // For EF Core
    }

    private TokenUsage(int inputTokens, int outputTokens)
    {
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
    }

    public static TokenUsage Zero => new(inputTokens: 0, outputTokens: 0);

    public static ErrorOr<TokenUsage> Create(int inputTokens, int outputTokens)
    {
        if (inputTokens < 0 || outputTokens < 0)
        {
            return Error.Validation
            (
                code: "TokenUsage.Negative",
                description: "Token counts cannot be negative."
            );
        }

        return new TokenUsage(inputTokens, outputTokens);
    }

    public static TokenUsage FromDatabase(int inputTokens, int outputTokens)
    {
        if (inputTokens < 0 || outputTokens < 0)
            throw new DomainException("Database contained negative token counts.");

        return new TokenUsage(inputTokens, outputTokens);
    }

    public TokenUsage Add(TokenUsage other) => new
    (
        inputTokens: InputTokens + other.InputTokens,
        outputTokens: OutputTokens + other.OutputTokens
    );
}