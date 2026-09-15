using Puluj.Admin.Docker;

namespace Puluj.Admin.Tests;

public class DockerJsonTests
{
    // Real lines of `docker ps -a --format '{{json .}}'` / `docker stats --no-stream --format '{{json .}}'` (Docker 29, compose 5.5).
    private const string PsApi = """{"Command":"\"dotnet Puluj.Api.dll\"","CreatedAt":"2026-09-15 06:02:24 +0300 EEST","HealthStatus":"none","ID":"625160471dc3","Image":"puluj-api","Labels":"com.docker.compose.config-hash=15bd68f2,com.docker.compose.container-number=1,com.docker.compose.depends_on=postgis:service_healthy:false,migrate:service_completed_successfully:false,com.docker.compose.project.config_files=C:\\repos\\Puluj\\deploy\\docker-compose.yml,C:\\repos\\Puluj\\deploy\\docker-compose.override.yml,com.docker.compose.project=puluj,com.docker.compose.service=api,com.docker.compose.version=5.5.1","LocalVolumes":"1","Mounts":"puluj_logs","Names":"puluj-api-1","Networks":"puluj_default","Platform":{"architecture":"amd64","os":"linux"},"Ports":"0.0.0.0:8080-\u003e8080/tcp","RunningFor":"31 minutes ago","Size":"32.8kB (virtual 266MB)","State":"running","Status":"Up 30 minutes"}""";
    private const string PsMigrate = """{"Command":"\"dotnet Puluj.Worker…\"","CreatedAt":"2026-09-15 06:02:23 +0300 EEST","ID":"86bbfc7e2914","Image":"puluj-worker","Labels":"com.docker.compose.container-number=1,com.docker.compose.project=puluj,com.docker.compose.service=migrate","Names":"puluj-migrate-1","State":"exited","Status":"Exited (0) 30 minutes ago"}""";
    private const string StatsApi = """{"BlockIO":"0B / 324kB","CPUPerc":"0.20%","Container":"625160471dc3560d28480bf54c5720f066981dc0d2900981eeae09afce5f9377","ID":"625160471dc3","MemPerc":"0.62%","MemUsage":"196.5MiB / 31.16GiB","Name":"puluj-api-1","NetIO":"146MB / 93.4MB","PIDs":"31"}""";

    [Fact]
    public void ParsePs_reads_id_names_state_labels_and_created_at()
    {
        var row = DockerJson.ParsePs(PsApi);

        Assert.NotNull(row);
        Assert.Equal("625160471dc3", row.Id);
        Assert.Equal("puluj-api-1", row.Name);
        Assert.Equal("puluj-api", row.Image);
        Assert.Equal("running", row.State);
        Assert.Equal("Up 30 minutes", row.Status);
        Assert.Equal("puluj", row.Label(DockerCommands.ProjectLabel));
        Assert.Equal("api", row.Label(DockerCommands.ServiceLabel));
        Assert.Equal("1", row.Label(DockerCommands.NumberLabel));
        Assert.Equal(new DateTimeOffset(2026, 9, 15, 6, 2, 24, TimeSpan.FromHours(3)), row.CreatedAt);
    }

    [Fact]
    public void ParseLabels_keeps_values_with_equals_signs_and_backslashes()
    {
        var labels = DockerJson.ParseLabels("a=1,com.docker.compose.project.config_files=C:\\x\\y.yml,b=x=y,broken,=novalue");

        Assert.Equal("1", labels["a"]);
        Assert.Equal("C:\\x\\y.yml", labels["com.docker.compose.project.config_files"]);
        Assert.Equal("x=y", labels["b"]);
        Assert.False(labels.ContainsKey("broken"));
        Assert.False(labels.ContainsKey(""));
    }

    [Fact]
    public void ParseStats_reads_cpu_and_memory()
    {
        var row = DockerJson.ParseStats(StatsApi);

        Assert.NotNull(row);
        Assert.Equal("625160471dc3", row.Id);
        Assert.Equal("puluj-api-1", row.Name);
        Assert.Equal(0.20, row.CpuPercent);
        Assert.Equal((long)Math.Round(196.5 * 1024 * 1024), row.MemoryBytes);
        Assert.Equal((long)Math.Round(31.16 * 1024 * 1024 * 1024), row.MemoryLimitBytes);
    }

    [Theory]
    [InlineData("0B", 0L)]
    [InlineData("324kB", 324_000L)]
    [InlineData("146MB", 146_000_000L)]
    [InlineData("12.3MiB", 12897485L)]
    [InlineData("7.6GiB", 8160437862L)]
    [InlineData("1.5 GiB", 1610612736L)]
    public void ParseSize_handles_decimal_and_binary_units(string text, long expected)
    {
        Assert.Equal(expected, DockerJson.ParseSize(text));
    }

    [Theory]
    [InlineData("--")]
    [InlineData("")]
    [InlineData("MiB")]
    [InlineData("12 parsecs")]
    public void ParseSize_returns_null_for_garbage(string text)
    {
        Assert.Null(DockerJson.ParseSize(text));
    }

    [Fact]
    public void ParsePercent_and_created_at_tolerate_odd_input()
    {
        Assert.Equal(1.23, DockerJson.ParsePercent("1.23%"));
        Assert.Equal(0, DockerJson.ParsePercent("0%"));
        Assert.Null(DockerJson.ParsePercent("--"));
        Assert.Null(DockerJson.ParseCreatedAt("yesterday"));
        Assert.Null(DockerJson.ParseCreatedAt(""));
        Assert.Equal(new DateTimeOffset(2026, 9, 15, 3, 2, 24, TimeSpan.Zero), DockerJson.ParseCreatedAt("2026-09-15 06:02:24 +0300 EEST")!.Value.ToUniversalTime());
    }

    [Fact]
    public void ParseLines_skips_blank_and_broken_lines()
    {
        var rows = DockerJson.ParseLines($"{PsApi}\n\n{{not json}}\r\n{PsMigrate}\n", DockerJson.ParsePs);

        Assert.Equal(["625160471dc3", "86bbfc7e2914"], rows.Select(r => r.Id));
        Assert.Equal("exited", rows[1].State);
        Assert.Equal("migrate", rows[1].Label(DockerCommands.ServiceLabel));
    }
}
