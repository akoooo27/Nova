namespace Chat.Domain.ModelCatalog.ValueObjects;

public sealed record LlmModelProfile
{
    public ModelName Name { get; }

    public ModelDescription Description { get; }

    public ContextWindow ContextWindow { get; }

    public ModelCapabilities Capabilities { get; }

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