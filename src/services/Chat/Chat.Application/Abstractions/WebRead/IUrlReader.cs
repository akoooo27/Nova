namespace Chat.Application.Abstractions.WebRead;

public interface IUrlReader
{
    Task<ReadPage> ReadAsync(Uri url, CancellationToken cancellationToken);
}

public sealed record ReadPage
(
    Uri Url,
    string? Title,
    string Markdown
);

public sealed record ReadUrlResponse
(
    bool Available,
    ReadPage? Page,
    string? Note
);