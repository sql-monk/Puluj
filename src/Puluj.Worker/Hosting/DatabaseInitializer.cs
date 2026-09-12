using Microsoft.EntityFrameworkCore;
using Puluj.Infrastructure.Persistence;
using Puluj.Infrastructure.Seeding;

namespace Puluj.Worker.Hosting;

/// <summary>Applies migrations and runs seeders before collectors start. Uses a Postgres advisory lock so two workers never race.</summary>
public sealed class DatabaseInitializer(
    IDbContextFactory<PulujDbContext> factory,
    IEnumerable<ISeeder> seeders,
    ILogger<DatabaseInitializer> logger) : IHostedService
{
    private const long LockKey = 0x50554C554A; // "PULUJ"

    public async Task StartAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Database.OpenConnectionAsync(ct);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_lock({LockKey})", ct);
        try
        {
            var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
            if (pending.Count > 0)
            {
                logger.LogInformation("Applying {Count} migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
                await db.Database.MigrateAsync(ct);
            }

            foreach (var seeder in seeders.OrderBy(s => s.Order))
            {
                logger.LogInformation("Seeding: {Seeder}", seeder.GetType().Name);
                await seeder.SeedAsync(db, ct);
            }
        }
        finally
        {
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_unlock({LockKey})", ct);
            await db.Database.CloseConnectionAsync();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
