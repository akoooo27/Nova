namespace Chat.Domain.ModelCatalog.ValueObjects;

public sealed record ModelCapabilities
{
    public bool SupportsVision { get; private init; }

    public bool SupportsReasoning { get; private init; }

    public bool SupportsToolCalling { get; private init; }

    private ModelCapabilities()
    {
        // For EF Core
    }

    private ModelCapabilities(bool supportsVision, bool supportsReasoning, bool supportsToolCalling)
    {
        SupportsVision = supportsVision;
        SupportsReasoning = supportsReasoning;
        SupportsToolCalling = supportsToolCalling;
    }

    public static ModelCapabilities None => new
    (
        supportsVision: false,
        supportsReasoning: false,
        supportsToolCalling: false
    );

    public static ModelCapabilities Create
    (
        bool supportsVision,
        bool supportsReasoning,
        bool supportsToolCalling
    ) => new
    (
        supportsVision: supportsVision,
        supportsReasoning: supportsReasoning,
        supportsToolCalling: supportsToolCalling
    );
}