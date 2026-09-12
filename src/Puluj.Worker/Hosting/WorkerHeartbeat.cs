using Puluj.Infrastructure.Settings;

namespace Puluj.Worker.Hosting;

/// <summary>Writes Runtime:Worker:Heartbeat every 30 s so the settings UI can tell whether a Worker is running at all.</summary>
public sealed class WorkerHeartbeat(SettingsStore settings, TimeProvider clock, ILogger<WorkerHeartbeat> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                await settings.SetStatusAsync("Worker:Heartbeat", clock.GetUtcNow().ToString("O"), ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogDebug(ex, "Heartbeat write failed");
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }
}
