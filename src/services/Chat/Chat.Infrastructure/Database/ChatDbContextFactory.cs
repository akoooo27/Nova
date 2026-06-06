using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

using Shared.Infrastructure.DomainEvents;

using SharedKernel;

namespace Chat.Infrastructure.Database;

internal sealed class ChatDbContextFactory : IDesignTimeDbContextFactory<ChatDbContext>
{
    private const string ConnectionName = "chat-db";
    private const string FallbackConnectionString = "Host=localhost;Port=5432;Database=chat-db";

    public ChatDbContext CreateDbContext(string[] args)
    {
        using ConfigurationManager configuration = new();
        configuration
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables();

        string connectionString = args.FirstOrDefault()
            ?? configuration.GetConnectionString(ConnectionName)
            ?? FallbackConnectionString;

        DbContextOptionsBuilder<ChatDbContext> optionsBuilder = new();
        optionsBuilder
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention();

        return new(optionsBuilder.Options, new NoOpDomainEventsDispatcher());
    }

    private sealed class NoOpDomainEventsDispatcher : IDomainEventsDispatcher
    {
        public Task DispatchAsync(
            IEnumerable<IDomainEvent> domainEvents,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}