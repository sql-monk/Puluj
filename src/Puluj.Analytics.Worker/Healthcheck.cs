namespace Puluj.Analytics.Worker;

/// <summary>`dotnet Puluj.Analytics.Worker.dll --healthcheck`: GET /health of the instance listening on ASPNETCORE_URLS (first URL) and exit 0 when it answers 200.</summary>
public static class Healthcheck
{
    public static async Task<int> RunAsync()
    {
        var urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://localhost:8082";
        var url = urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0]
            .Replace("+", "localhost").Replace("*", "localhost").Replace("0.0.0.0", "localhost").TrimEnd('/');
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var response = await client.GetAsync($"{url}/health");
            var body = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"{(int)response.StatusCode} {body}");
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Console.WriteLine(ex.Message);
            return 1;
        }
    }
}
