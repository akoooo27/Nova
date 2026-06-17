using Chat.Application.Abstractions.WebRead;

namespace Chat.Application.Tests.Turns;

internal sealed class FakeUrlReader
(
    ReadPage? page = null,
    Exception? exception = null
) : IUrlReader
{
    public int Calls { get; private set; }

    public Uri? Url { get; private set; }

    public CancellationToken CancellationToken { get; private set; }

    public Task<ReadPage> ReadAsync(Uri url, CancellationToken cancellationToken)
    {
        Calls++;
        Url = url;
        CancellationToken = cancellationToken;

        return exception is null
            ? Task.FromResult(page ?? new ReadPage(url, Title: null, Markdown: string.Empty))
            : Task.FromException<ReadPage>(exception);
    }
}