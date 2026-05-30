using Duende.Bff.EntityFramework;

using Microsoft.EntityFrameworkCore;

namespace BFF.Database;

internal sealed class BffSessionDbContext(DbContextOptions<BffSessionDbContext> options)
    : SessionDbContext<BffSessionDbContext>(options);