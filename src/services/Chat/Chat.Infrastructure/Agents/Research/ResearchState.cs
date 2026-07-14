namespace Chat.Infrastructure.Agents.Research;

internal sealed record ResearchFinding
(
    string Url,
    string Title,
    string Notes
);

internal sealed record ResearchState
(
    string Brief,
    IReadOnlyList<string> History,
    IReadOnlyList<string> OpenQuestions,
    IReadOnlyList<string> CandidateUrls,
    IReadOnlyList<ResearchFinding> Findings,
    IReadOnlyList<string> AttemptedUrls,
    int Round,
    int SearchesUsed,
    int SourcesRead,
    int NextSequence
)
{
    public static ResearchState Start(string brief, IReadOnlyList<string> history) => new
    (
        Brief: brief,
        History: history,
        OpenQuestions: [],
        CandidateUrls: [],
        Findings: [],
        AttemptedUrls: [],
        Round: 0,
        SearchesUsed: 0,
        SourcesRead: 0,
        NextSequence: 1
    );
}