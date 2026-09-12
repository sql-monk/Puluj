using Puluj.Infrastructure.Persistence;

namespace Puluj.Infrastructure.Seeding;

/// <summary>Idempotent data seeder, run by the Worker after migrations.</summary>
public interface ISeeder
{
    /// <summary>Lower runs first.</summary>
    int Order { get; }
    Task SeedAsync(PulujDbContext db, CancellationToken ct);
}
