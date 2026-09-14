using System.Text;
using Puluj.Contracts;

namespace Puluj.Admin;

/// <summary>
/// Reads the rolling Serilog files every service writes into the shared logs directory
/// (`api-2026-09-13.log`, `worker-…`, `admin-…`). Only the tail is read, from the end of the file backwards,
/// so a day-long log of hundreds of megabytes costs the same as a small one.
/// </summary>
public sealed class LogReader(IConfiguration configuration, IHostEnvironment env)
{
    private const int MaxLines = 2000;
    private const long MaxScanBytes = 8 * 1024 * 1024;

    public string Directory
    {
        get
        {
            var configured = configuration["Logs:Directory"] ?? "../../logs";
            return Path.GetFullPath(Path.IsPathRooted(configured) ? configured : Path.Combine(env.ContentRootPath, configured));
        }
    }

    public IReadOnlyList<LogFileDto> Files()
    {
        var dir = Directory;
        if (!System.IO.Directory.Exists(dir))
        {
            return [];
        }
        return System.IO.Directory.EnumerateFiles(dir, "*.log")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => new LogFileDto(f.Name, ServiceOf(f.Name), f.Length, f.LastWriteTimeUtc))
            .ToList();
    }

    /// <summary>Last <paramref name="lines"/> lines of a file, optionally only those containing <paramref name="filter"/> and/or at the given level.</summary>
    public LogTailDto Tail(string file, int lines, string? filter, string? level)
    {
        if (file.Contains("..") || file.Contains('/') || file.Contains('\\') || !file.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Invalid file name.", nameof(file));
        }
        var path = Path.Combine(Directory, file);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(file);
        }
        lines = Math.Clamp(lines, 1, MaxLines);
        var levelTag = string.IsNullOrWhiteSpace(level) ? null : $"[{LevelTag(level)}]";

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var length = stream.Length;
        var start = Math.Max(0, length - MaxScanBytes);
        stream.Position = start;
        var buffer = new byte[length - start];
        stream.ReadExactly(buffer);
        var text = Encoding.UTF8.GetString(buffer);
        var all = text.Split('\n');
        if (start > 0 && all.Length > 0)
        {
            all = all[1..]; // the first line is most likely cut in the middle
        }

        // Serilog writes exceptions as continuation lines that do not start with a timestamp: glue them to their entry.
        var entries = new List<string>();
        foreach (var raw in all)
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }
            if (entries.Count > 0 && !(line.Length > 4 && char.IsDigit(line[0]) && line[4] == '-'))
            {
                entries[^1] += "\n" + line;
            }
            else
            {
                entries.Add(line);
            }
        }

        IEnumerable<string> selected = entries;
        if (levelTag is not null)
        {
            selected = selected.Where(e => e.Contains(levelTag, StringComparison.Ordinal));
        }
        if (!string.IsNullOrWhiteSpace(filter))
        {
            selected = selected.Where(e => e.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }
        var list = selected.ToList();
        var truncated = start > 0 || list.Count > lines;
        return new LogTailDto(file, list.Count > lines ? list[^lines..] : list, truncated, length);
    }

    public static string ServiceOf(string fileName)
    {
        var dash = fileName.IndexOf('-');
        return dash > 0 ? fileName[..dash] : fileName;
    }

    private static string LevelTag(string level) => level.ToLowerInvariant() switch
    {
        "verbose" or "vrb" => "VRB",
        "debug" or "dbg" => "DBG",
        "information" or "info" or "inf" => "INF",
        "warning" or "warn" or "wrn" => "WRN",
        "error" or "err" => "ERR",
        "fatal" or "ftl" => "FTL",
        var other => other.ToUpperInvariant(),
    };
}
