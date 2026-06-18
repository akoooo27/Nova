using Chat.CleanupWorker.Authorization;
using Chat.CleanupWorker.Jobs;
using Chat.CleanupWorker.Options;

using Hangfire;
using Hangfire.PostgreSql;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Chat.CleanupWorker;

internal static class DependencyInjection
{
    private const string DashboardPath = "/admin/hangfire";
    private const string RecurringJobId = "temporary-chat-cleanup";

    public static WebApplicationBuilder AddTemporaryChatCleanup(this WebApplicationBuilder builder)
    {
        builder.Services
            .AddOptions<TemporaryChatCleanupOptions>()
            .Bind(builder.Configuration.GetSection(TemporaryChatCleanupOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        string connectionString = builder.Configuration.GetConnectionString("chat-db")
            ?? throw new InvalidOperationException("Connection string 'chat-db' is required.");

        builder.Services.AddHangfire(configuration => configuration
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage
            (
                postgres => postgres.UseNpgsqlConnection(connectionString),
                new PostgreSqlStorageOptions { SchemaName = "hangfire" }
            )
        );

        builder.Services.AddHangfireServer(options => options.WorkerCount = 2);

        builder.Services.AddScoped<TemporaryChatCleanupJob>();

        return builder;
    }

    public static WebApplication MapTemporaryChatCleanupDashboard(this WebApplication app)
    {
        string dashboardSecret = app.Configuration[HangfireDashboardSecretFilter.SecretConfigurationKey]
            ?? throw new InvalidOperationException(
                $"'{HangfireDashboardSecretFilter.SecretConfigurationKey}' is required.");

        app.MapHangfireDashboard(DashboardPath, new DashboardOptions
        {
            AsyncAuthorization = [new HangfireDashboardSecretFilter(dashboardSecret)],
            DisplayStorageConnectionString = false
        });

        return app;
    }

    public static WebApplication UseTemporaryChatCleanupRecurringJob(this WebApplication app)
    {
        IRecurringJobManager recurringJobs = app.Services.GetRequiredService<IRecurringJobManager>();
        TemporaryChatCleanupOptions options = app.Services
            .GetRequiredService<IOptions<TemporaryChatCleanupOptions>>().Value;

        recurringJobs.AddOrUpdate<TemporaryChatCleanupJob>
        (
            recurringJobId: RecurringJobId,
            methodCall: job => job.RunAsync(CancellationToken.None),
            cronExpression: options.Cron,
            options: new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc }
        );

        return app;
    }
}