using Puluj.Domain.Entities;

namespace Puluj.Admin.Tests;

public class WorkerStatusTests
{
    private static Dictionary<string, AppSetting> Settings(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => new AppSetting { Key = p.Key, Value = p.Value });

    private const string Status = """
        {"instance":"processor-616c2ab99756","host":"616c2ab99756","roles":["processing"],"version":"1.0.0","builtAt":"2026-09-15T05:50:00+00:00",
         "startedAt":"2026-09-15T06:02:30+00:00","at":"2026-09-15T10:00:00+00:00","pid":1,"workingSetBytes":123456789,"cpuPercent":3.5,"threads":30,
         "processing":{"concurrency":2,"processed":120,"skipped":3,"failed":1,"retried":0,"retriedTransient":0,"perMinute1":4.0,"perMinute5":3.2,
           "parse":{"samples":10,"meanMs":12.0,"p50Ms":10.0,"p90Ms":20.0,"maxMs":40.0},"lock":{"samples":10,"meanMs":1.0,"p50Ms":1.0,"p90Ms":2.0,"maxMs":3.0},
           "store":{"samples":10,"meanMs":30.0,"p50Ms":25.0,"p90Ms":50.0,"maxMs":90.0},"total":{"samples":10,"meanMs":43.0,"p50Ms":36.0,"p90Ms":72.0,"maxMs":133.0},
           "lastProcessedAt":"2026-09-15T09:59:58+00:00","lastRawMessageId":4242,"claims":[{"rawMessageId":4243,"since":"2026-09-15T09:59:59+00:00"}]},
         "llm":{"enabled":true,"model":"claude-opus-5","calls":7,"failures":0}}
        """;

    [Fact]
    public void Reads_the_camel_case_document_the_worker_writes()
    {
        var all = Settings(("Runtime:Worker:processor-616c2ab99756:Status", Status));

        var status = OpsEndpoints.WorkerStatus(all, "processor-616c2ab99756");

        Assert.NotNull(status);
        Assert.Equal("processor-616c2ab99756", status.Instance);
        Assert.Equal(["processing"], status.Roles);
        Assert.Equal(2, status.Processing!.Concurrency);
        Assert.Equal(120, status.Processing.Processed);
        Assert.Equal(36.0, status.Processing.Total.P50Ms);
        Assert.Equal(4243, Assert.Single(status.Processing.Claims).RawMessageId);
        Assert.True(status.Llm!.Enabled);
        Assert.Null(status.Llm.PausedUntil);
        Assert.Null(status.Paused);
    }

    [Fact]
    public void Missing_or_broken_document_gives_null_not_an_error()
    {
        Assert.Null(OpsEndpoints.WorkerStatus(Settings(), "processor-616c2ab99756"));
        Assert.Null(OpsEndpoints.WorkerStatus(Settings(("Runtime:Worker:x:Status", "")), "x"));
        Assert.Null(OpsEndpoints.WorkerStatus(Settings(("Runtime:Worker:x:Status", "{not json")), "x"));
        Assert.Null(OpsEndpoints.WorkerStatus(Settings(("Runtime:Worker:x:Status", "[1,2]")), "x"));
    }

    [Fact]
    public void Key_lookup_is_case_insensitive_like_the_heartbeats()
    {
        var all = Settings(("runtime:worker:Processor-616c2ab99756:status", Status));

        Assert.NotNull(OpsEndpoints.WorkerStatus(all, "processor-616c2ab99756"));
    }

    [Fact]
    public void Heartbeats_skip_the_legacy_key_and_forget_dead_instances()
    {
        var now = new DateTimeOffset(2026, 9, 15, 10, 0, 0, TimeSpan.Zero);
        var all = Settings(
            ("Runtime:Worker:Heartbeat", now.AddSeconds(-5).ToString("O")),
            ("Runtime:Worker:analytics:Heartbeat", now.AddSeconds(-20).ToString("O")),
            ("Runtime:Worker:processor-a:Heartbeat", now.AddSeconds(-40).ToString("O")),
            ("Runtime:Worker:processor-b:Heartbeat", now.AddMinutes(-20).ToString("O")),
            ("Runtime:Worker:broken:Heartbeat", "not a date"));

        var beats = OpsEndpoints.WorkerHeartbeats(all, now);

        Assert.Equal(["processor-a", "analytics"], beats.Select(b => b.Name)); // oldest first
    }
}
