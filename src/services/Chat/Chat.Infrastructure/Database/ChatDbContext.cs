using Chat.Application.Abstractions.Database;
using Chat.Domain.AgentRuns;
using Chat.Domain.Chats;
using Chat.Domain.FavoriteModels;
using Chat.Domain.ModelCatalog;
using Chat.Domain.Personalizations;
using Chat.Domain.Projects;
using Chat.Domain.SharedChats;
using Chat.Infrastructure.Users.Models;

using MassTransit;

using Microsoft.EntityFrameworkCore;

using Shared.Infrastructure.DomainEvents;

using SharedKernel;

namespace Chat.Infrastructure.Database;

public sealed class ChatDbContext(
    DbContextOptions<ChatDbContext> options,
    IDomainEventsDispatcher dispatcher)
    : DbContext(options), IUnitOfWork
{
    public DbSet<LlmProvider> LlmProviders => Set<LlmProvider>();

    internal DbSet<UserReadModel> Users => Set<UserReadModel>();

    public DbSet<FavoriteModel> FavoriteModels => Set<FavoriteModel>();

    public DbSet<ChatThread> ChatThreads => Set<ChatThread>();

    public DbSet<SharedChat> SharedChats => Set<SharedChat>();

    public DbSet<Personalization> Personalizations => Set<Personalization>();

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<AgentRun> AgentRuns => Set<AgentRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ChatDbContext).Assembly);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        List<IDomainEvent> domainEvents = ExtractDomainEvents();

        int result = await base.SaveChangesAsync(cancellationToken);

        await PublishDomainEventsAsync(domainEvents);

        return result;
    }

    private async Task PublishDomainEventsAsync(IEnumerable<IDomainEvent> domainEvents)
    {
        await dispatcher.DispatchAsync(domainEvents);
    }

    private List<IDomainEvent> ExtractDomainEvents()
    {
        IEnumerable<IAggregateRoot> aggregateRoots = ChangeTracker
            .Entries()
            .Select(entry => entry.Entity)
            .OfType<IAggregateRoot>();

        List<IDomainEvent> domainEvents = aggregateRoots
            .SelectMany(entity =>
            {
                List<IDomainEvent> domainEvents = entity.DomainEvents.ToList();

                entity.ClearDomainEvents();

                return domainEvents;
            })
            .ToList();

        return domainEvents;
    }
}