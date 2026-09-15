using Microsoft.Extensions.Options;
using Puluj.Infrastructure.Settings;

namespace Puluj.Worker.Hosting;

/// <summary>
/// Writes Runtime:Worker:{Name}:Heartbeat every 30 s so the admin panel can tell which Worker instances are running
/// (each container has its own name, see WorkerOptions). The key is removed on a clean shutdown; an instance that dies
/// leaves a stale heartbeat behind, which the panel shows as "down" for a quarter of an hour.
/// </summary>
public sealed class WorkerHeartbeat(SettingsStore settings, IOptions<WorkerOptions> options, TimeProvider clock, ILogger<WorkerHeartbeat> logger) : BackgroundService
{
    private string StatusKey => $"Worker:{options.Value.InstanceName}:Heartbeat";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                await settings.SetStatusAsync(StatusKey, clock.GetUtcNow().ToString("O"), ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogDebug(ex, "Heartbeat write failed");
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await settings.SetStatusAsync(StatusKey, null, cts.Token);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Heartbeat removal failed");
        }
    }
}
