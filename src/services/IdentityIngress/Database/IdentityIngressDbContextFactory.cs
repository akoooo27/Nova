using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace IdentityIngress.Database;

internal sealed class IdentityIngressDbContextFactory : IDesignTimeDbContextFactory<IdentityIngressDbContext>
{
    private const string ConnectionName = "identity-ingress-db";
    private const string FallbackConnectionString = "Host=localhost;Port=5432;Database=identity-ingress-db";

    public IdentityIngressDbContext CreateDbContext(string[] args)
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

        DbContextOptionsBuilder<IdentityIngressDbContext> optionsBuilder = new();
        optionsBuilder
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention();

        return new(optionsBuilder.Options);
    }
}