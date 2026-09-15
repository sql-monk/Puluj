using System.Net;
using Puluj.Processing.Llm;

namespace Puluj.Processing.Tests.Llm;

public class LlmBreakerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 15, 4, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Billing_auth_and_rejected_requests_pause_for_the_configured_time()
    {
        var breaker = new LlmBreaker(TimeSpan.FromMinutes(15));
        Assert.False(breaker.IsOpen(T0, out _));
        Assert.Equal(TimeSpan.FromMinutes(15), breaker.Trip(HttpStatusCode.BadRequest, "invalid_request_error: credit balance is too low", T0));
        Assert.True(breaker.IsOpen(T0.AddMinutes(14), out var reason));
        Assert.Contains("credit balance", reason);
        Assert.False(breaker.IsOpen(T0.AddMinutes(15), out _)); // the next message tries again
        Assert.Equal(TimeSpan.FromMinutes(15), breaker.PauseFor(HttpStatusCode.Unauthorized));
        Assert.Equal(TimeSpan.FromMinutes(15), breaker.PauseFor(HttpStatusCode.Forbidden));
    }

    [Fact]
    public void Rate_limit_pauses_for_a_minute_and_server_errors_do_not_pause()
    {
        var breaker = new LlmBreaker(TimeSpan.FromMinutes(15));
        Assert.Equal(LlmBreaker.RateLimitPause, breaker.Trip(HttpStatusCode.TooManyRequests, "rate_limit_error", T0));
        Assert.True(breaker.IsOpen(T0.AddSeconds(59), out _));
        Assert.False(breaker.IsOpen(T0.AddSeconds(60), out _));
        Assert.Null(breaker.Trip(HttpStatusCode.InternalServerError, "overloaded", T0));
        Assert.Null(breaker.Trip(HttpStatusCode.ServiceUnavailable, "overloaded", T0));
        Assert.False(breaker.IsOpen(T0.AddSeconds(60), out _));
    }

    [Fact]
    public void Success_clears_the_pause_and_its_reason()
    {
        var breaker = new LlmBreaker(TimeSpan.FromMinutes(15));
        breaker.Trip(HttpStatusCode.Forbidden, "permission_error", T0);
        breaker.Reset();
        Assert.False(breaker.IsOpen(T0, out var reason));
        Assert.Null(reason);
    }
}
