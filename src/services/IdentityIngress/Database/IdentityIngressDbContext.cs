using MassTransit;

using Microsoft.EntityFrameworkCore;

namespace IdentityIngress.Database;

internal sealed class IdentityIngressDbContext(DbContextOptions<IdentityIngressDbContext> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}