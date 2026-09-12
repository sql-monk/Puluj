using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Puluj.Infrastructure.Seeding;

/// <summary>Resolves seed file paths and reads JSON with relaxed options (comments, trailing commas).</summary>
public sealed class SeedFiles(IOptions<SeedOptions> options, IHostEnvironment env)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public string Root
    {
        get
        {
            var dir = options.Value.DataDirectory;
            if (Path.IsPathRooted(dir))
            {
                return dir;
            }
            // Walk up from content root so `dotnet run` from src/Puluj.Worker finds <repo>/data.
            var probe = env.ContentRootPath;
            for (var i = 0; i < 5 && probe is not null; i++)
            {
                var candidate = Path.Combine(probe, dir);
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
                probe = Path.GetDirectoryName(probe);
            }
            return Path.Combine(env.ContentRootPath, dir);
        }
    }

    public string Resolve(params string[] parts) => Path.Combine([Root, .. parts]);

    public async Task<T?> ReadAsync<T>(string relative, CancellationToken ct)
    {
        var path = Resolve(relative);
        if (!File.Exists(path))
        {
            return default;
        }
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, Json, ct);
    }
}
