using Microsoft.AspNetCore.SignalR;
using Puluj.Api.Hubs;
using Puluj.Infrastructure.Messaging;

namespace Puluj.Api.Services;

/// <summary>Turns Postgres NOTIFY events from the Worker into SignalR pushes. The database stays the source of truth: only ids travel over NOTIFY.</summary>
public sealed class NotifyBridge(
    PgNotifyListener listener,
    IHubContext<MapHub, IMapClient> hub,
    SnapshotService snapshots,
    ReferenceCache refs,
    ILogger<NotifyBridge> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await refs.Ready.WaitAsync(ct);
        await foreach (var evt in listener.ListenAsync(ct))
        {
            try
            {
                logger.LogDebug("Event {Type} {Id}", evt.Type, evt.Id);
                switch (evt.Type)
                {
                    case PulujEventType.TrackUpserted:
                        if (await snapshots.TrackAsync(evt.Id, ct) is { } track)
                        {
                            await hub.Clients.All.TrackUpserted(track);
                        }
                        break;
                    case PulujEventType.TrackClosed:
                        if (await snapshots.TrackAsync(evt.Id, ct) is { } closed)
                        {
                            await hub.Clients.All.TrackClosed(closed);
                        }
                        break;
                    case PulujEventType.ObservationCreated:
                        if (await snapshots.ObservationAsync(evt.Id, ct) is { } observation)
                        {
                            await hub.Clients.All.ObservationCreated(observation);
                        }
                        break;
                    case PulujEventType.AlertChanged:
                        if (await snapshots.AlertAsync(evt.Id, ct) is { } alert)
                        {
                            await hub.Clients.All.AlertChanged(alert);
                        }
                        break;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to push {Type} {Id}", evt.Type, evt.Id);
            }
        }
    }
}
