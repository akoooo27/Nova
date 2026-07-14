using System.ComponentModel.DataAnnotations;

namespace Chat.Infrastructure.Options;

public sealed class ResearchOptions
{
    public const string SectionName = "Research";

    [Range(1, 10)]
    public int MaxRounds { get; init; } = 3;

    [Range(1, 50)]
    public int MaxSearches { get; init; } = 12;

    [Range(1, 40)]
    public int MaxSourcesToRead { get; init; } = 10;
}