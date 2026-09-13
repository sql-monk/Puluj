using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Puluj.Domain.Enums;
using Puluj.Infrastructure;
using Puluj.Infrastructure.Ingestion;
using Puluj.Infrastructure.Persistence;
using Puluj.Infrastructure.Seeding;
using Puluj.Processing;
using Puluj.Processing.Indexes;
using Puluj.Processing.Pipeline;
using Testcontainers.PostgreSql;

namespace Puluj.Integration.Tests;

/// <summary>
/// End-to-end over a real PostGIS database: seed -> ingest raw messages -> parse -> correlate -> replay.
/// Uses PULUJ_TEST_CONNECTION when set (e.g. a local PostGIS), otherwise starts a postgis container via Docker.
/// </summary>
public sealed class PipelineTests : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private ServiceProvider? _services;
    private string? _connectionString;

    public async Task InitializeAsync()
    {
        _connectionString = Environment.GetEnvironmentVariable("PULUJ_TEST_CONNECTION");
        if (string.IsNullOrEmpty(_connectionString))
        {
            try
            {
                _container = new PostgreSqlBuilder("postgis/postgis:17-3.5").WithDatabase("puluj_test").Build();
                await _container.StartAsync();
                _connectionString = _container.GetConnectionString();
            }
            catch (Exception)
            {
                _container = null; // no Docker: tests below become no-ops
                return;
            }
        }

        var repoRoot = FindRepoRoot();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Puluj"] = _connectionString,
            ["Seed:DataDirectory"] = Path.Combine(repoRoot, "data"),
            ["Seed:SeedGazetteer"] = "true",
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddMetrics();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<IHostEnvironment>(new TestEnvironment(repoRoot));
        services.AddPulujInfrastructure(config);
        services.AddPulujProcessing(config);
        _services = services.BuildServiceProvider();

        await using var db = await _services.GetRequiredService<IDbContextFactory<PulujDbContext>>().CreateDbContextAsync();
        await db.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS postgis");
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE track_targets, target_track_revisions, target_tracks, targets, air_alerts, processing_errors, raw_messages RESTART IDENTITY CASCADE");
        foreach (var seeder in _services.GetServices<ISeeder>().OrderBy(s => s.Order))
        {
            await seeder.SeedAsync(db, CancellationToken.None);
        }
        await _services.GetRequiredService<IndexProvider>().RefreshAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        if (_services is not null)
        {
            await _services.DisposeAsync();
        }
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    [Fact]
    public async Task Raw_messages_become_targets_tracks_and_revisions()
    {
        if (_services is null)
        {
            return; // neither PULUJ_TEST_CONNECTION nor Docker available
        }
        var factory = _services.GetRequiredService<IDbContextFactory<PulujDbContext>>();
        var ingestor = _services.GetRequiredService<RawMessageIngestor>();
        var processor = _services.GetRequiredService<RawMessageProcessor>();
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-40);

        int sourceId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            sourceId = await db.Sources.Where(s => s.Code == "tg_kpszsu").Select(s => s.SourceId).SingleAsync();
        }

        var first = await ingestor.IngestAsync(Msg(sourceId, "m1", t0, "Шахеди на Сумщині курсом на Полтавщину."), "tg_kpszsu", CancellationToken.None);
        var second = await ingestor.IngestAsync(Msg(sourceId, "m2", t0.AddMinutes(30), "БпЛА на Полтавщині у південному напрямку."), "tg_kpszsu", CancellationToken.None);
        var duplicate = await ingestor.IngestAsync(Msg(sourceId, "m1", t0, "Шахеди на Сумщині курсом на Полтавщину."), "tg_kpszsu", CancellationToken.None);
        Assert.True(first.IsNew && second.IsNew);
        Assert.False(duplicate.IsNew); // idempotent on (source, source_message_id)

        Assert.Equal(1, await processor.ProcessAsync(first.RawMessageId!.Value, CancellationToken.None));
        Assert.Equal(1, await processor.ProcessAsync(second.RawMessageId!.Value, CancellationToken.None));
        Assert.Equal(0, await processor.ProcessAsync(second.RawMessageId!.Value, CancellationToken.None)); // already processed

        await using (var db = await factory.CreateDbContextAsync())
        {
            var track = await db.TargetTracks.SingleAsync();
            Assert.Equal(TrackStatus.Active, track.Status);
            Assert.Equal(2, track.TargetCount);
            Assert.Equal("Полтавська область", await db.Places.Where(p => p.PlaceId == track.LastLocationPlaceId).Select(p => p.Name).SingleAsync());
            Assert.Null(track.TrackGeometry); // two adjacent oblasts overlap: a line between their centres is not a route
            Assert.Equal(2, await db.TargetTrackRevisions.CountAsync(r => r.TargetTrackId == track.TargetTrackId));
            Assert.Equal(2, await db.TrackTargets.CountAsync());
            Assert.Equal(ProcessingStatus.Processed, (await db.RawMessages.FindAsync(first.RawMessageId))!.ProcessingStatus);

            // Replay: before the second message only the first revision exists.
            var replayAt = t0.AddMinutes(10);
            var revision = await db.TargetTrackRevisions.Where(r => r.RevisionAt <= replayAt).OrderByDescending(r => r.RevisionAt).FirstAsync();
            Assert.Equal(1, revision.TargetCount);
        }

        // A levelled alert stated in text (Kyiv oblast administration style) becomes an AirAlert interval with its level,
        // and the oblast-wide "відбій" closes it.
        var yellow = await ingestor.IngestAsync(Msg(sourceId, "m3", t0.AddMinutes(35), "🟡 Броварський район — повітряна тривога, жовтий рівень: Дронова загроза (жовтий рівень)"), "tg_kpszsu", CancellationToken.None);
        Assert.True(await processor.ProcessAsync(yellow.RawMessageId!.Value, CancellationToken.None) >= 1);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var alert = await db.AirAlerts.SingleAsync(a => a.SourceAlertId.StartsWith("text:"));
            Assert.Equal(AirAlertLevel.Yellow, alert.Level);
            Assert.Null(alert.EndedAt);
            Assert.Equal(1, await db.TargetTracks.CountAsync()); // the named cause is not a sighting
        }
        var cancel = await ingestor.IngestAsync(Msg(sourceId, "m4", t0.AddMinutes(50), "Київська область — відбій повітряної тривоги."), "tg_kpszsu", CancellationToken.None);
        Assert.Equal(1, await processor.ProcessAsync(cancel.RawMessageId!.Value, CancellationToken.None));
        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.NotNull((await db.AirAlerts.SingleAsync(a => a.SourceAlertId.StartsWith("text:"))).EndedAt);
        }
    }

    private static IncomingMessage Msg(int sourceId, string id, DateTimeOffset at, string text) => new()
    {
        SourceId = sourceId,
        SourceMessageId = id,
        PublishedAt = at,
        RawText = text,
        RawPayload = JsonDocument.Parse("{\"kind\":\"test\"}"),
    };

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Puluj.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found");
    }

    private sealed class TestEnvironment(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Puluj.Integration.Tests";
        public string ContentRootPath { get; set; } = root;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
