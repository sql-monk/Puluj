using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Puluj.Infrastructure.Settings;

/// <summary>
/// Makes app_settings rows part of IConfiguration (last in the chain, so they override appsettings.json and env vars).
/// Polls the table every few seconds; when values change, IOptionsMonitor consumers get notified and, in the Worker,
/// collectors restart with the new values. Tolerates a missing table (before the first migration) and DB outages.
/// </summary>
public sealed class DbConfigurationProvider(string connectionString, TimeSpan pollInterval) : ConfigurationProvider, IDisposable
{
    private Timer? _timer;
    private bool _disposed;

    public override void Load()
    {
        TryLoad();
        _timer ??= new Timer(_ => TryLoad(), null, pollInterval, pollInterval);
    }

    private void TryLoad()
    {
        if (_disposed)
        {
            return;
        }
        try
        {
            using var conn = new NpgsqlConnection(connectionString);
            conn.Open();
            // Runtime:* rows are status written by the Worker (heartbeat, Telegram login state), not configuration.
            using var cmd = new NpgsqlCommand("SELECT key, value FROM app_settings WHERE value IS NOT NULL AND key NOT LIKE 'Runtime:%'", conn);
            using var reader = cmd.ExecuteReader();
            var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            while (reader.Read())
            {
                data[reader.GetString(0)] = reader.GetString(1);
            }
            if (!SameAs(data))
            {
                Data = data;
                OnReload();
            }
        }
        catch (Exception)
        {
            // Table not there yet or database unreachable: keep whatever we had (possibly nothing).
        }
    }

    private bool SameAs(Dictionary<string, string?> other) =>
        Data.Count == other.Count && other.All(kv => Data.TryGetValue(kv.Key, out var v) && v == kv.Value);

    public void Dispose()
    {
        _disposed = true;
        _timer?.Dispose();
    }
}

public sealed class DbConfigurationSource(string connectionString, TimeSpan pollInterval) : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) => new DbConfigurationProvider(connectionString, pollInterval);
}

public static class DbConfigurationExtensions
{
    /// <summary>Adds app_settings as the highest-priority configuration source. Call after the default sources are in place.</summary>
    public static IConfigurationBuilder AddPulujDatabaseSettings(this IConfigurationBuilder builder, TimeSpan? pollInterval = null)
    {
        var connectionString = builder.Build().GetConnectionString(DependencyInjection.ConnectionStringName);
        if (!string.IsNullOrEmpty(connectionString))
        {
            builder.Add(new DbConfigurationSource(connectionString, pollInterval ?? TimeSpan.FromSeconds(5)));
        }
        return builder;
    }
}
