using LlmMockService.Core.Configuration;
using LlmMockService.Core.Deployments;
using Microsoft.Extensions.Time.Testing;

namespace LlmMockService.Core.Tests.Deployments;

public class DeploymentCatalogTests
{
    private readonly DeploymentCatalog _catalog = new(TestOptions.Valid(), new FakeTimeProvider());

    [Fact]
    public void Unknown_name_falls_back_to_default_profile_without_quota_or_faults()
    {
        var resolved = _catalog.Resolve("some-model");

        Assert.False(resolved.IsConfigured);
        Assert.Equal("default", resolved.Profile.Name);
        Assert.True(resolved.Quota.IsUnlimited);
        Assert.Same(FaultOptions.None, resolved.Faults);
    }

    [Fact]
    public void Configured_name_is_case_insensitive_and_has_its_quota()
    {
        var resolved = _catalog.Resolve("LIMITED");

        Assert.True(resolved.IsConfigured);
        Assert.Equal(2, resolved.Quota.Peek().RequestLimit);
    }

    [Fact]
    public void Quota_state_is_shared_across_resolves()
    {
        Assert.True(_catalog.Resolve("limited").Quota.TryAcquire(1).Allowed);
        Assert.True(_catalog.Resolve("limited").Quota.TryAcquire(1).Allowed);

        Assert.False(_catalog.Resolve("limited").Quota.TryAcquire(1).Allowed);
    }

    [Fact]
    public void Reset_refills_quotas_and_clears_overrides()
    {
        _catalog.Resolve("limited").Quota.TryAcquire(1);
        _catalog.Resolve("limited").Quota.TryAcquire(1);
        _catalog.SetFaultOverride("limited", new FaultOptions { ServerErrorRate = 1 });

        _catalog.Reset();

        var resolved = _catalog.Resolve("limited");
        Assert.True(resolved.Quota.TryAcquire(1).Allowed);
        Assert.Equal(0, resolved.Faults.ServerErrorRate);
    }

    [Fact]
    public void Fault_override_applies_to_unknown_names_and_can_be_cleared()
    {
        _catalog.SetFaultOverride("ad-hoc", new FaultOptions { ServiceUnavailableRate = 1 });

        Assert.Equal(1, _catalog.Resolve("ad-hoc").Faults.ServiceUnavailableRate);
        Assert.True(_catalog.ClearFaultOverride("ad-hoc"));
        Assert.Same(FaultOptions.None, _catalog.Resolve("ad-hoc").Faults);
        Assert.False(_catalog.ClearFaultOverride("ad-hoc"));
    }

    [Fact]
    public void Invalid_fault_override_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => _catalog.SetFaultOverride("x", new FaultOptions { ServerErrorRate = 2 }));
    }

    [Fact]
    public void Describe_lists_configured_and_overridden_names_only()
    {
        _catalog.Resolve("never-configured");
        _catalog.SetFaultOverride("overridden", new FaultOptions { StallRate = 0.5 });

        var names = _catalog.Describe().Select(d => d.Name).ToArray();

        Assert.Equal(["limited", "overridden"], names);
    }

    [Fact]
    public void Invalid_options_fail_construction_with_all_errors()
    {
        var options = TestOptions.Valid();
        options.DefaultProfile = "missing";

        var ex = Assert.Throws<ArgumentException>(() => new DeploymentCatalog(options, TimeProvider.System));
        Assert.Contains("missing", ex.Message, StringComparison.Ordinal);
    }
}
