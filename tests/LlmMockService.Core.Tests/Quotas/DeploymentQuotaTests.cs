using LlmMockService.Core.Quotas;
using Microsoft.Extensions.Time.Testing;

namespace LlmMockService.Core.Tests.Quotas;

public class DeploymentQuotaTests
{
    private readonly FakeTimeProvider _time = new();

    [Fact]
    public void Requests_beyond_rpm_are_denied_with_exact_retry_after()
    {
        var quota = new DeploymentQuota(requestsPerMinute: 3, tokensPerMinute: null, _time);

        Assert.True(quota.TryAcquire(0).Allowed);
        Assert.True(quota.TryAcquire(0).Allowed);
        Assert.True(quota.TryAcquire(0).Allowed);
        var denied = quota.TryAcquire(0);

        Assert.False(denied.Allowed);
        Assert.Equal(TimeSpan.FromSeconds(20), denied.RetryAfter); // 3 per minute refills one every 20 s
    }

    [Fact]
    public void Tokens_refill_continuously()
    {
        var quota = new DeploymentQuota(null, tokensPerMinute: 1000, _time);

        Assert.True(quota.TryAcquire(600).Allowed);
        var denied = quota.TryAcquire(600);
        Assert.False(denied.Allowed);
        Assert.Equal(12, denied.RetryAfter.TotalSeconds, precision: 6); // 200 short at 1000/60 per second

        _time.Advance(TimeSpan.FromSeconds(12));

        Assert.True(quota.TryAcquire(600).Allowed);
    }

    [Fact]
    public void Denied_request_consumes_nothing_from_either_bucket()
    {
        var quota = new DeploymentQuota(requestsPerMinute: 10, tokensPerMinute: 100, _time);

        Assert.False(quota.TryAcquire(101).Allowed);
        var snapshot = quota.Peek();

        Assert.Equal(10, snapshot.RemainingRequests);
        Assert.Equal(100, snapshot.RemainingTokens);
    }

    [Fact]
    public void Request_larger_than_capacity_reports_a_full_window()
    {
        var quota = new DeploymentQuota(null, tokensPerMinute: 100, _time);

        var decision = quota.TryAcquire(150);

        Assert.False(decision.Allowed);
        Assert.Equal(TimeSpan.FromMinutes(1), decision.RetryAfter);
    }

    [Fact]
    public void Refill_never_exceeds_capacity()
    {
        var quota = new DeploymentQuota(requestsPerMinute: 5, tokensPerMinute: null, _time);
        quota.TryAcquire(0);

        _time.Advance(TimeSpan.FromHours(1));

        Assert.Equal(5, quota.Peek().RemainingRequests);
    }

    [Fact]
    public void Peek_does_not_consume()
    {
        var quota = new DeploymentQuota(requestsPerMinute: 1, tokensPerMinute: null, _time);

        quota.Peek();
        quota.Peek();

        Assert.True(quota.TryAcquire(0).Allowed);
    }

    [Fact]
    public void Unlimited_quota_always_allows()
    {
        var quota = new DeploymentQuota(null, 0, _time);

        Assert.True(quota.IsUnlimited);
        Assert.All(Enumerable.Range(0, 1000), _ => Assert.True(quota.TryAcquire(1_000_000).Allowed));
        Assert.Null(quota.Peek().RequestLimit);
        Assert.Null(quota.Peek().TokenLimit);
    }
}
