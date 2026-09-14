using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Puluj.Domain.Entities;
using Puluj.Domain.Enums;
using Puluj.Infrastructure;
using Puluj.Infrastructure.Messaging;
using Puluj.Infrastructure.Persistence;
using Puluj.Processing.Indexes;
using Puluj.Processing.Parsing;
using Puluj.Processing.Structured;
using Puluj.Processing.Text;

namespace Puluj.Processing.Pipeline;

/// <summary>Runs after targets of one RawMessage are saved (deduplication, correlation). Implemented in the correlation stage.</summary>
public interface ITargetSink
{
    /// <summary>Runs inside the message transaction; events added to <paramref name="events"/> are published after commit.</summary>
    Task OnTargetsAsync(PulujDbContext db, IReadOnlyList<Target> targets, Source source, ICollection<PulujEvent> events, CancellationToken ct);
}

/// <summary>
/// Processes one RawMessage end-to-end (spec §4): structured payload or text -> facts -> targets -> sink.
/// All writes for a message happen in one transaction; failures are recorded in ProcessingError and retried up to MaxAttempts.
/// </summary>
public sealed class RawMessageProcessor(
    IDbContextFactory<PulujDbContext> factory,
    INormalizer normalizer,
    IParser parser,
    IIndexes indexes,
    TargetBuilder builder,
    AlertsInUaHandler alertsHandler,
    IEnumerable<ITargetSink> sinks,
    INotifyPublisher notifier,
    IOptions<ProcessingOptions> options,
    PulujMetrics metrics,
    TimeProvider clock,
    ILogger<RawMessageProcessor> logger)
{
    public async Task<int> ProcessAsync(long rawMessageId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var raw = await db.RawMessages.Include(r => r.Source).FirstOrDefaultAsync(r => r.RawMessageId == rawMessageId, ct);
        if (raw is null || raw.ProcessingStatus != ProcessingStatus.Pending)
        {
            return 0;
        }
        var source = raw.Source!;
        var sw = Stopwatch.StartNew();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            List<Target> targets;
            if (AlertsInUaHandler.CanHandle(raw))
            {
                targets = await alertsHandler.HandleAsync(db, raw, source, ct);
            }
            else if (!string.IsNullOrWhiteSpace(raw.RawText))
            {
                targets = await ParseTextAsync(raw, source, ct);
            }
            else
            {
                raw.ProcessingStatus = ProcessingStatus.Skipped;
                raw.ProcessedAt = clock.GetUtcNow();
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return 0;
            }

            var parsedMs = sw.ElapsedMilliseconds;
            db.Targets.AddRange(targets);
            raw.ProcessingStatus = ProcessingStatus.Processed;
            raw.ProcessedAt = clock.GetUtcNow();
            raw.Attempts++;
            await db.SaveChangesAsync(ct);

            var events = new List<PulujEvent>();
            foreach (var sink in sinks)
            {
                await sink.OnTargetsAsync(db, targets, source, events, ct);
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            logger.LogDebug("RawMessage {Id}: parse {ParseMs} ms, store + sinks {SinkMs} ms", raw.RawMessageId, parsedMs, sw.ElapsedMilliseconds - parsedMs);
            foreach (var evt in events)
            {
                await notifier.PublishAsync(evt, ct);
            }

            if (targets.Count == 0)
            {
                metrics.ParserUnmatched(source.Code);
                logger.LogDebug("RawMessage {Id}: no facts in \"{Text}\"", raw.RawMessageId, Truncate(raw.RawText, 120));
            }
            foreach (var o in targets)
            {
                metrics.TargetCreated(source.Code, o.IdentificationMethod.ToString());
            }
            logger.LogInformation("RawMessage {Id} ({Source}): {Count} target(s) in {Ms} ms", raw.RawMessageId, source.Code, targets.Count, sw.ElapsedMilliseconds);
            return targets.Count;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(CancellationToken.None);
            await RecordFailureAsync(rawMessageId, source, ex);
            return 0;
        }
    }

    private async Task<List<Target>> ParseTextAsync(RawMessage raw, Source source, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var normalized = normalizer.Normalize(raw.RawText!);
        var normalizeMs = sw.ElapsedMilliseconds;
        var ctx = new ParseContext(source.SourceId, normalized.Language, HomeRegionOf(source), raw.PublishedAt);
        var facts = await parser.ParseAsync(normalized, ctx, ct);
        var parseMs = sw.ElapsedMilliseconds - normalizeMs;
        var targets = facts.Select(f => builder.Build(f, raw, source, f.ParserVersion, f.Method, normalized.Language)).ToList();
        logger.LogDebug("RawMessage {Id}: normalize {NormalizeMs} ms, rules {RulesMs} ms, build {BuildMs} ms", raw.RawMessageId, normalizeMs, parseMs, sw.ElapsedMilliseconds - normalizeMs - parseMs);
        return targets;
    }

    /// <summary>Source config may name a home region ("Київська область") or give a place id; used to disambiguate settlement names.</summary>
    private int? HomeRegionOf(Source source)
    {
        if (source.Config is null)
        {
            return null;
        }
        var root = source.Config.RootElement;
        if (root.TryGetProperty("homeRegionPlaceId", out var idEl) && idEl.ValueKind == JsonValueKind.Number)
        {
            return idEl.GetInt32();
        }
        if (root.TryGetProperty("homeRegion", out var nameEl) && nameEl.ValueKind == JsonValueKind.String && nameEl.GetString() is { Length: > 0 } name)
        {
            var n = normalizer.Normalize(name);
            if (n.Segments.Count > 0)
            {
                var match = new PlaceMatcher(indexes.Gazetteer)
                    .Match(n.Segments[0], new ParseContext(0, "uk", null), new HashSet<int>(), new HashSet<(int, int)>())
                    .FirstOrDefault();
                return match is null ? null : (indexes.Gazetteer.RegionOf(match.Place) ?? match.Place).PlaceId;
            }
        }
        return null;
    }

    private async Task RecordFailureAsync(long rawMessageId, Source source, Exception ex)
    {
        metrics.ProcessingError("process");
        logger.LogError(ex, "RawMessage {Id} failed", rawMessageId);
        try
        {
            await using var db = await factory.CreateDbContextAsync();
            var raw = await db.RawMessages.FirstAsync(r => r.RawMessageId == rawMessageId);
            raw.Attempts++;
            if (raw.Attempts >= options.Value.MaxAttempts)
            {
                raw.ProcessingStatus = ProcessingStatus.Failed;
                raw.ProcessedAt = clock.GetUtcNow();
            }
            db.ProcessingErrors.Add(new ProcessingError
            {
                RawMessageId = rawMessageId,
                SourceId = source.SourceId,
                Stage = "process",
                Message = ex.Message,
                Exception = ex.ToString(),
                OccurredAt = clock.GetUtcNow(),
            });
            await db.SaveChangesAsync();
        }
        catch (Exception inner)
        {
            logger.LogError(inner, "Could not record processing error for RawMessage {Id}", rawMessageId);
        }
    }

    private static string Truncate(string? s, int max) => s is null ? "" : s.Length <= max ? s : s[..max] + "…";
}
