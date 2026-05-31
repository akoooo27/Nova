namespace Chat.Domain.ModelCatalog.ValueObjects;

public sealed record ModelCapabilities
{
    public bool SupportsVision { get; }

    public bool SupportsReasoning { get; }

    public bool SupportsToolCalling { get; }

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