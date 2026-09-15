using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Puluj.Infrastructure.Persistence;

/// <summary>
/// Holds a read-only service (Api, Admin) back until the database schema matches its model. The Worker applies
/// migrations; a service built from newer code and started before it (compose bringing `api` up next to `migrate`,
/// a dev run of the Api before the Worker) would otherwise answer every request and every NOTIFY with
/// "column does not exist" — thousands of error lines a minute for as long as nobody notices.
/// </summary>
public static class SchemaReadiness
{
    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(5);

    /// <summary>Returns when there are no pending migrations; logs the wait every few seconds until then.</summary>
    public static async Task WaitForMigrationsAsync(IServiceProvider services, ILogger logger, CancellationToken ct)
    {
        var factory = services.GetRequiredService<IDbContextFactory<PulujDbContext>>();
        while (true)
        {
            try
            {
                await using var db = await factory.CreateDbContextAsync(ct);
                var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
                if (pending.Count == 0)
                {
                    return;
                }
                logger.LogError("Schema is behind the code: waiting for {Count} migration(s) to be applied by the worker: {Migrations}", pending.Count, string.Join(", ", pending));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Database not reachable yet ({Error}); retrying", ex.Message);
            }
            await Task.Delay(Retry, ct);
        }
    }
}
