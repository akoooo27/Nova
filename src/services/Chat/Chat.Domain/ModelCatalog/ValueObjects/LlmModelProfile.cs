namespace Chat.Domain.ModelCatalog.ValueObjects;

public sealed record LlmModelProfile
{
    public ModelName Name { get; private init; } = default!;

    public ModelDescription Description { get; private init; } = default!;

    public ContextWindow ContextWindow { get; private init; } = default!;

    public ModelCapabilities Capabilities { get; private init; } = default!;

    private LlmModelProfile()
    {
        // For EF Core
    }

    private LlmModelProfile
    (
        ModelName name,
        ModelDescription description,
        ContextWindow contextWindow,
        ModelCapabilities capabilities
    )
    {
        Name = name;
        Description = description;
        ContextWindow = contextWindow;
        Capabilities = capabilities;
    }

    public static LlmModelProfile Create
    (
        ModelName name,
        ModelDescription description,
        ContextWindow contextWindow,
        ModelCapabilities capabilities
    ) => new
    (
        name: name,
        description: description,
        contextWindow: contextWindow,
        capabilities: capabilities
    );
}