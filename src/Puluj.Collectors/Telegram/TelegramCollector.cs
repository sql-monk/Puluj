using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Puluj.Domain.Entities;
using Puluj.Domain.Enums;
using Puluj.Infrastructure.Ingestion;
using Puluj.Infrastructure.Settings;
using TL;

namespace Puluj.Collectors.Telegram;

/// <summary>
/// One MTProto session (WTelegramClient) serving every enabled Telegram source. Backfills recent history on start,
/// then stores every new/edited channel post as a RawMessage. Edits become separate RawMessages ("{id}:e{editDate}")
/// so the original text is never lost (spec §5: RawMessage is immutable).
/// </summary>
public sealed class TelegramCollector(
    IOptionsMonitor<TelegramOptions> options,
    RawMessageIngestor ingestor,
    CollectorStateStore states,
    SettingsStore settings,
    ILogger<TelegramCollector> logger) : ICollector
{
    public const string StatusKey = "Telegram:Status";

    public string Name => "telegram";

    private readonly Dictionary<long, (Source Source, string Username)> _channels = [];
    private TelegramOptions _o = new();

    public bool Handles(Source source) => options.CurrentValue.Enabled && source.Type == SourceType.Telegram && Username(source) is not null;

    public async Task RunAsync(IReadOnlyList<Source> sources, CancellationToken ct)
    {
        _o = options.CurrentValue;
        var o = _o;
        _channels.Clear();
        if (o.ApiId == 0 || string.IsNullOrWhiteSpace(o.ApiHash) || string.IsNullOrWhiteSpace(o.Phone))
        {
            logger.LogWarning("Telegram ApiId/ApiHash/Phone missing (Collectors:Telegram:*); collector idle");
            await settings.SetStatusAsync(StatusKey, "missing_credentials", ct);
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return;
        }
        // WTelegramClient opens the session file *before* validating api_hash; a bad hash would throw with the file
        // handle leaked, so validate here and stay idle instead of crash-looping.
        if (!IsValidApiHash(o.ApiHash))
        {
            logger.LogWarning("Telegram api_hash must be 32 hex characters; collector idle");
            await settings.SetStatusAsync(StatusKey, "error: api_hash має бути 32 hex-символи (перевірте значення з my.telegram.org)", ct);
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(o.SessionPath))!);
        WTelegram.Helpers.Log = (level, msg) => logger.Log(level switch
        {
            >= 4 => LogLevel.Error,
            3 => LogLevel.Warning,
            2 => LogLevel.Information,
            _ => LogLevel.Debug,
        }, "WTelegram: {Message}", msg);

        try
        {
            await RunSessionAsync(sources, o, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            await settings.SetStatusAsync(StatusKey, "error: " + ex.Message, CancellationToken.None);
            throw;
        }
    }

    private async Task RunSessionAsync(IReadOnlyList<Source> sources, TelegramOptions o, CancellationToken ct)
    {
        using var client = new WTelegram.Client(Config);
        var manager = client.WithUpdateManager(OnUpdate, o.SessionPath + ".updates");
        await settings.SetStatusAsync(StatusKey, "connecting", ct);
        TL.User user;
        try
        {
            user = await client.LoginUserIfNeeded();
        }
        catch (Exception ex)
        {
            await settings.SetStatusAsync(StatusKey, "error: " + ex.Message, CancellationToken.None);
            throw;
        }
        logger.LogInformation("Telegram: logged in as {User} (id {Id})", user.username ?? user.first_name, user.id);
        await settings.SetStatusAsync(StatusKey, $"logged_in: {user.first_name} {user.last_name} (@{user.username})".Trim(), ct);

        // Let the update manager know about our dialogs so peers resolve; then resolve each configured channel.
        await manager.LoadDialogs(await client.Messages_GetAllDialogs());
        foreach (var source in sources)
        {
            var username = Username(source)!;
            try
            {
                var resolved = await client.Contacts_ResolveUsername(username);
                if (resolved.Chat is not Channel channel)
                {
                    logger.LogWarning("Telegram: @{Username} is not a channel; skipping {Source}", username, source.Code);
                    continue;
                }
                if (o.AutoJoin && channel.flags.HasFlag(Channel.Flags.left))
                {
                    await client.Channels_JoinChannel(channel);
                    logger.LogInformation("Telegram: joined @{Username}", username);
                }
                _channels[channel.id] = (source, username);
                await BackfillAsync(client, channel, source, username, ct);
            }
            catch (RpcException ex)
            {
                logger.LogWarning(ex, "Telegram: cannot set up @{Username} ({Source})", username, source.Code);
                await states.MarkFailureAsync(source.SourceId, ex.Message, ct);
            }
        }
        logger.LogInformation("Telegram: listening to {Count} channel(s)", _channels.Count);
        await settings.SetStatusAsync(StatusKey, $"listening: {_channels.Count} channel(s) as @{user.username ?? user.first_name}", ct);

        // Keep the session alive until cancelled; WTelegramClient reconnects on its own.
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }
        finally
        {
            manager.SaveState(o.SessionPath + ".updates");
            await settings.SetStatusAsync(StatusKey, "stopped", CancellationToken.None);
        }
    }

    private string? Config(string what)
    {
        var o = _o;
        return what switch
        {
            "api_id" => o.ApiId.ToString(),
            "api_hash" => o.ApiHash,
            "phone_number" => o.Phone,
            "password" => o.Password,
            "session_pathname" => o.SessionPath,
            "verification_code" => WaitForVerificationCode(),
            _ => null,
        };
    }

    /// <summary>
    /// First login only. The code can arrive three ways: the admin UI (app_settings key Collectors:Telegram:VerificationCode,
    /// consumed and cleared), the configuration value, or a file &lt;SessionPath&gt;.code. Waits up to 10 minutes.
    /// </summary>
    private string? WaitForVerificationCode()
    {
        var o = _o;
        const string key = "Collectors:Telegram:VerificationCode";
        var codeFile = o.SessionPath + ".code";
        logger.LogWarning("Telegram asks for the login code sent to {Phone}. Enter it in the admin UI, set {Key}, or write it into {File}. Waiting up to 10 minutes.", o.Phone, key, codeFile);
        settings.SetStatusAsync(StatusKey, "waiting_code", CancellationToken.None).GetAwaiter().GetResult();
        var deadline = DateTime.UtcNow.AddMinutes(10);
        while (DateTime.UtcNow < deadline)
        {
            var fromDb = settings.GetAsync(key, CancellationToken.None).GetAwaiter().GetResult();
            if (!string.IsNullOrWhiteSpace(fromDb))
            {
                settings.SetAsync(new Dictionary<string, string?> { [key] = null }, CancellationToken.None).GetAwaiter().GetResult();
                return fromDb.Trim();
            }
            if (!string.IsNullOrWhiteSpace(o.VerificationCode))
            {
                return o.VerificationCode;
            }
            if (File.Exists(codeFile))
            {
                var code = File.ReadAllText(codeFile).Trim();
                File.Delete(codeFile);
                if (code.Length > 0)
                {
                    return code;
                }
            }
            Thread.Sleep(2000);
        }
        throw new TimeoutException("Telegram verification code was not provided in time.");
    }

    private async Task BackfillAsync(WTelegram.Client client, Channel channel, Source source, string username, CancellationToken ct)
    {
        var state = await states.GetAsync(source.SourceId, ct);
        var minId = int.TryParse(state.LastSourceMessageId?.Split(':')[0], out var last) ? last : 0;
        var history = await client.Messages_GetHistory(channel, limit: Math.Clamp(_o.BackfillLimit, 1, 100), min_id: minId);
        var messages = history.Messages.OfType<Message>().OrderBy(m => m.id).ToList();
        var stored = 0;
        foreach (var m in messages)
        {
            if (await StoreAsync(m, source, username, ct))
            {
                stored++;
            }
        }
        var newest = messages.LastOrDefault();
        await states.MarkSuccessAsync(source.SourceId, newest?.id.ToString(), newest is null ? null : ToUtc(newest.date), null, ct);
        logger.LogInformation("Telegram: @{Username} backfill {Stored}/{Total} new (min_id {MinId})", username, stored, messages.Count, minId);
    }

    private async Task OnUpdate(Update update)
    {
        try
        {
            switch (update)
            {
                case UpdateNewChannelMessage { message: Message m }:
                    await HandleAsync(m, isEdit: false);
                    break;
                case UpdateEditChannelMessage { message: Message m }:
                    await HandleAsync(m, isEdit: true);
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Telegram: update handling failed");
        }
    }

    private async Task HandleAsync(Message m, bool isEdit)
    {
        if (m.peer_id is not PeerChannel pc || !_channels.TryGetValue(pc.channel_id, out var entry))
        {
            return;
        }
        var (source, username) = entry;
        var stored = await StoreAsync(m, source, username, CancellationToken.None);
        if (stored)
        {
            await states.MarkSuccessAsync(source.SourceId, isEdit ? null : m.id.ToString(), ToUtc(m.date), null, CancellationToken.None);
        }
    }

    private async Task<bool> StoreAsync(Message m, Source source, string username, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(m.message) && m.media is null)
        {
            return false; // service messages
        }
        var payload = TelegramMessagePayload.From(m, username);
        var sourceMessageId = payload.EditDate is null ? m.id.ToString() : $"{m.id}:e{payload.EditDate.Value.ToUnixTimeSeconds()}";
        var result = await ingestor.IngestAsync(new IncomingMessage
        {
            SourceId = source.SourceId,
            SourceMessageId = sourceMessageId,
            PublishedAt = payload.EditDate ?? payload.Date,
            RawText = m.message,
            RawPayload = payload.ToDocument(),
            Url = $"https://t.me/{username}/{m.id}",
        }, source.Code, ct);
        return result.IsNew;
    }

    private static string? Username(Source source)
    {
        if (source.Config is null || !source.Config.RootElement.TryGetProperty("channel", out var c))
        {
            return null;
        }
        var s = NormalizeUsername(c.GetString());
        return string.IsNullOrEmpty(s) ? null : s;
    }

    /// <summary>Accepts "@name", "name", "https://t.me/name" or "t.me/name/123" and returns "name".</summary>
    public static string? NormalizeUsername(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        var s = raw.Trim();
        var idx = s.IndexOf("t.me/", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            s = s[(idx + 5)..];
        }
        s = s.TrimStart('@').Split('/', '?', '#')[0].Trim();
        return s.Length == 0 ? null : s;
    }

    public static bool IsValidApiHash(string? hash) => hash is { Length: 32 } && hash.All(Uri.IsHexDigit);

    private static DateTimeOffset ToUtc(DateTime d) => new(DateTime.SpecifyKind(d, DateTimeKind.Utc));
}
