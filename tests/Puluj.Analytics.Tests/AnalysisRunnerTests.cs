using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Puluj.Analytics.Analysis;
using Puluj.Analytics.Persistence;
using Puluj.Analytics.Reporting;
using Puluj.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Puluj.Analytics.Tests;

/// <summary>
/// The runner over a real database: the pipeline's schema (for raw_messages / sources) plus the analytics schema.
/// Uses PULUJ_TEST_CONNECTION when set, otherwise a postgis container via Docker; without either the tests are no-ops.
/// </summary>
public sealed class AnalysisRunnerTests : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private ServiceProvider? _services;
    private int _a, _b;

    public async Task InitializeAsync()
    {
        var cs = Environment.GetEnvironmentVariable("PULUJ_TEST_CONNECTION");
        if (string.IsNullOrEmpty(cs))
        {
            try
            {
                _container = new PostgreSqlBuilder("postgis/postgis:17-3.5").WithDatabase("puluj_analytics_test").Build();
                await _container.StartAsync();
                cs = _container.GetConnectionString();
            }
            catch (Exception)
            {
                _container = null;
                return;
            }
        }
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Puluj"] = cs,
            ["Analytics:SafetyLag"] = "00:00:00",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddMetrics();
        services.AddSingleton<IConfiguration>(config);
        services.AddDbContextFactory<PulujDbContext>(o => Puluj.Infrastructure.DependencyInjection.ConfigureDbContext(o, cs));
        services.AddPulujAnalytics(config);
        _services = services.BuildServiceProvider();

        await using (var db = await _services.GetRequiredService<IDbContextFactory<PulujDbContext>>().CreateDbContextAsync())
        {
            await db.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS postgis");
            db.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));
            await db.Database.MigrateAsync();
            await db.Database.ExecuteSqlRawAsync("DELETE FROM raw_messages WHERE source_id IN (SELECT source_id FROM sources WHERE code LIKE 'test_analytics_%')");
            await db.Database.ExecuteSqlRawAsync("DELETE FROM sources WHERE code LIKE 'test_analytics_%'");
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO sources (code, name, type, trust_level, priority, enabled) VALUES
                    ('test_analytics_a', 'Test A', 2, 0.9, 10, true), ('test_analytics_b', 'Test B', 2, 0.5, 5, true)
                """);
            var ids = await db.Sources.Where(s => s.Code.StartsWith("test_analytics_")).OrderBy(s => s.Code).Select(s => s.SourceId).ToListAsync();
            (_a, _b) = (ids[0], ids[1]);
        }
        await using (var adb = await Factory.CreateDbContextAsync())
        {
            await adb.Database.MigrateAsync();
            await Reports.ResetAsync(CancellationToken.None);
            await adb.Database.ExecuteSqlRawAsync("DELETE FROM analytics.runs");
        }
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

    private IDbContextFactory<AnalyticsDbContext> Factory => _services!.GetRequiredService<IDbContextFactory<AnalyticsDbContext>>();
    private AnalysisRunner Runner => _services!.GetRequiredService<AnalysisRunner>();
    private AnalyticsReportService Reports => _services!.GetRequiredService<AnalyticsReportService>();

    private const string X = "Групи ударних БпЛА на Сумщині в районі Боромля, Лебедин та Недригайлів західним курсом на Полтавщину та Черкащину.";
    private const string Y = "Пуски керованих авіаційних бомб ворожою тактичною авіацією на південь Харківщини, будьте обережні в укриттях.";

    private async Task<long> InsertAsync(int source, string key, DateTimeOffset publishedAt, string text, long? channelId = null, string? forwardedFrom = null)
    {
        await using var db = await Factory.CreateDbContextAsync();
        var payload = channelId is null && forwardedFrom is null ? "{}" : $$"""{"channelId": {{(channelId?.ToString() ?? "null")}}, "forwardedFrom": {{(forwardedFrom is null ? "null" : $"\"{forwardedFrom}\"")}}}""";
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{source}|{key}|{text}")));
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO raw_messages (source_id, source_message_id, published_at, received_at, raw_text, raw_payload, hash, processing_status, attempts)
            VALUES ({source}, {key}, {publishedAt}, {DateTimeOffset.UtcNow.AddHours(-1)}, {text}, {payload}::jsonb, {hash}, 1, 1)
            """);
        return (await db.Database.SqlQuery<long>($"SELECT raw_message_id AS \"Value\" FROM raw_messages WHERE source_id = {source} AND source_message_id = {key}").ToListAsync()).Single();
    }

    private async Task<List<MessageCopy>> CopiesAsync()
    {
        await using var db = await Factory.CreateDbContextAsync();
        return await db.Copies.AsNoTracking().OrderBy(c => c.CopyPostKey).ThenBy(c => c.OriginalPostKey).ToListAsync();
    }

    [Fact]
    public async Task Finds_copies_forwards_and_late_originals_and_survives_reruns_and_reset()
    {
        if (_services is null)
        {
            return; // no database available
        }
        var t0 = DateTimeOffset.UtcNow.AddHours(-3);
        await InsertAsync(_a, "1", t0, X, channelId: 111);
        await InsertAsync(_b, "2", t0.AddMinutes(5), "🛵 " + X.ToUpperInvariant(), channelId: 222);
        await InsertAsync(_b, "3", t0.AddMinutes(40), X + " Додатковий коментар моніторингового каналу про ситуацію в області: слідкуйте за оновленнями, перебувайте в укриттях до відбою.", channelId: 222);
        await InsertAsync(_a, "4", t0.AddMinutes(10), Y, channelId: 111);
        await InsertAsync(_b, "5", t0.AddHours(1), X, channelId: 222, forwardedFrom: "channel 111");
        await InsertAsync(_b, "3:e1789433701", t0.AddMinutes(41), X + " Додатковий коментар моніторингового каналу про ситуацію в області: слідкуйте за оновленнями, перебувайте в укриттях до відбою (оновлено).", channelId: 222);

        var first = await Runner.RunOnceAsync(CancellationToken.None);
        Assert.True(first.Locked);
        Assert.Equal(6, first.Scanned);
        Assert.Equal(6, first.Fingerprinted);

        var copies = await CopiesAsync();
        Assert.Equal(3, copies.Count);
        Assert.All(copies, c => Assert.Equal((_b, _a, "1"), (c.CopySourceId, c.OriginalSourceId, c.OriginalPostKey)));
        Assert.All(copies, c => Assert.True(c.IsPrimary));
        Assert.Equal(CopyKind.Verbatim, copies.Single(c => c.CopyPostKey == "2").Kind);
        Assert.Equal(300, copies.Single(c => c.CopyPostKey == "2").DelaySeconds);
        var edited = copies.Single(c => c.CopyPostKey == "3");
        Assert.True(edited.Containment > 0.85 && edited.Jaccard < 0.9);
        Assert.EndsWith(":e1789433701", await PostKeyOfAsync(edited.CopyRawMessageId)); // the edit updated the row, no second row
        Assert.Equal(CopyKind.Forward, copies.Single(c => c.CopyPostKey == "5").Kind);

        // Nothing new: the second run scans nothing and changes nothing.
        var second = await Runner.RunOnceAsync(CancellationToken.None);
        Assert.Equal(0, second.Scanned);
        Assert.Equal(3, (await CopiesAsync()).Count);

        // A history load brings in an earlier post of A with the same text: it becomes the primary original of every copy.
        await InsertAsync(_a, "0", t0.AddMinutes(-20), X, channelId: 111);
        await Runner.RunOnceAsync(CancellationToken.None);
        copies = await CopiesAsync();
        Assert.Equal(6, copies.Count);
        Assert.All(copies.Where(c => c.OriginalPostKey == "0"), c => Assert.True(c.IsPrimary));
        Assert.All(copies.Where(c => c.OriginalPostKey == "1"), c => Assert.False(c.IsPrimary));

        var status = await Reports.StatusAsync(CancellationToken.None);
        Assert.True(status.Initialized);
        Assert.Equal(0, status.Backlog);
        Assert.Equal("ok", status.LastRun!.Status);
        Assert.Equal(7, status.MessagesIndexed);

        var report = await Reports.ReportAsync(14, CancellationToken.None);
        Assert.NotNull(report);
        var a = report.Sources.Single(s => s.Id == _a);
        var b = report.Sources.Single(s => s.Id == _b);
        Assert.Equal(3, a.Posts);
        Assert.Equal(3, a.CopiedBy);
        Assert.Equal(3, b.Posts);
        Assert.Equal(1, b.Edits);
        Assert.Equal(3, b.Copies);
        Assert.Equal(0, b.UniqueShare);
        Assert.Equal(1, b.ForwardsInternal);
        var pair = report.Pairs.Single(p => p.Count > 0);
        Assert.Equal((_b, _a, 3, 6, 1, 1), (pair.CopierId, pair.OriginalId, pair.Count, pair.CountAll, pair.Verbatim, pair.Forwards));
        var recent = await Reports.RecentAsync(10, _b, null, false, CancellationToken.None);
        Assert.Equal(6, recent.Count);
        Assert.All(recent, r => Assert.False(string.IsNullOrEmpty(r.CopyText)));
        Assert.Equal(3, (await Reports.RecentAsync(10, null, null, true, CancellationToken.None)).Count);
        Assert.All(await Reports.RecentAsync(10, null, CopyKind.Forward, false, CancellationToken.None), r => Assert.Equal("forward", r.Kind));

        var details = await Reports.PairAsync(_b, _a, 14, CancellationToken.None);
        Assert.NotNull(details);
        Assert.Equal((3, 6, 1, 1), (details.Count, details.CountAll, details.Verbatim, details.Forwards));
        Assert.Equal(3, details.Delays.Sum(d => d.Count));
        Assert.Equal(6, details.Recent.Count);
        Assert.Equal(pair.MedianDelaySeconds, details.MedianDelaySeconds);

        // Reset and rebuild from scratch: the same picture.
        await Reports.ResetAsync(CancellationToken.None);
        Assert.Empty(await CopiesAsync());
        var rebuilt = await Runner.RunOnceAsync(CancellationToken.None);
        Assert.Equal(7, rebuilt.Scanned);
        Assert.Equal(6, (await CopiesAsync()).Count);
    }

    private async Task<string> PostKeyOfAsync(long rawMessageId)
    {
        await using var db = await Factory.CreateDbContextAsync();
        return (await db.Database.SqlQuery<string>($"SELECT source_message_id AS \"Value\" FROM raw_messages WHERE raw_message_id = {rawMessageId}").ToListAsync()).Single();
    }
}
