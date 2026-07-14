using System.Globalization;
using System.Text;

namespace Chat.Infrastructure.Agents.Research;

internal static class ResearchPrompts
{
    public const string PlannerInstructions =
        "You are a research planner. Given a research brief, produce focused web search queries " +
        "that together cover the question. Output ONLY the queries, one per line, no numbering.";

    public const string CondenserInstructions =
        "You extract facts. Given a research brief and a page, list only the concrete facts, figures, " +
        "dates, and claims relevant to the brief, as short bullet lines. If nothing is relevant, output NOTHING.";

    public const string CriticInstructions =
        "You are a research critic. Given a brief and collected findings, decide whether the findings " +
        "suffice to answer the brief. If they suffice, output exactly DONE. Otherwise output ONLY new web " +
        "search queries that close the remaining gaps, one per line, no numbering.";

    public const string WriterInstructions =
        "You are a research writer. Write a well-structured markdown report answering the brief using ONLY " +
        "the provided findings. Cite sources inline as [n] matching the numbered source list, and end with " +
        "a '## Sources' section listing each source as '[n] Title — URL'. Be factual; note open questions.";

    public static string Planner(string brief, IReadOnlyList<string> history)
    {
        StringBuilder prompt = new();

        if (history.Count > 0)
        {
            prompt.AppendLine("Conversation context:");

            foreach (string line in history)
            {
                prompt.AppendLine(line);
            }

            prompt.AppendLine();
        }

        prompt.AppendLine("Research brief:");
        prompt.AppendLine(brief);

        return prompt.ToString();
    }

    public static string Condense(string brief, string url, string? title, string markdown)
    {
        const int maxPageChars = 6000;

        string page = markdown.Length <= maxPageChars
            ? markdown
            : markdown[..maxPageChars];

        return $"Research brief:\n{brief}\n\nPage: {title} ({url})\n\n{page}";
    }

    public static string Critic(string brief, IReadOnlyList<ResearchFinding> findings)
    {
        StringBuilder prompt = new();
        prompt.AppendLine(CultureInfo.InvariantCulture, $"Research brief:\n{brief}\n");
        prompt.AppendLine("Findings so far:");

        foreach (ResearchFinding finding in findings)
        {
            prompt.AppendLine(CultureInfo.InvariantCulture, $"- {finding.Title} ({finding.Url}): {finding.Notes}");
        }

        return prompt.ToString();
    }

    public static string Writer(string brief, IReadOnlyList<ResearchFinding> findings)
    {
        StringBuilder prompt = new();
        prompt.AppendLine(CultureInfo.InvariantCulture, $"Research brief:\n{brief}\n");
        prompt.AppendLine("Numbered sources and findings:");

        for (int i = 0; i < findings.Count; i++)
        {
            ResearchFinding finding = findings[i];
            prompt.AppendLine(CultureInfo.InvariantCulture, $"[{i + 1}] {finding.Title} — {finding.Url}");
            prompt.AppendLine(finding.Notes);
        }

        return prompt.ToString();
    }

    public static IReadOnlyList<string> ParseQueries(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith("DONE", StringComparison.OrdinalIgnoreCase))
            .Select(line => line.TrimStart('-', '*', ' ').Trim())
            .Where(line => line.Length > 2)
            .Take(5)
            .ToList();
}