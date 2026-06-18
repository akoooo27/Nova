using System.ComponentModel.DataAnnotations;

namespace Chat.CleanupWorker.Options;

internal sealed class TemporaryChatCleanupOptions
{
    public const string SectionName = "TemporaryChatCleanup";

    public TimeSpan RetentionPeriod { get; init; } = TimeSpan.FromDays(30);

    [Required]
    public string Cron { get; init; } = "0 3 * * *";
}