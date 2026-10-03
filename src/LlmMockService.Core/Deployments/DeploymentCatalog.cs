using System.Collections.Concurrent;
using LlmMockService.Core.Configuration;
using LlmMockService.Core.Latency;
using LlmMockService.Core.Quotas;

namespace LlmMockService.Core.Deployments;

public sealed record ResolvedDeployment(string Name, bool IsConfigured, LatencyProfile Profile, FaultOptions Faults, DeploymentQuota Quota);

public sealed record DeploymentState(
    string Name,
    bool IsConfigured,
    string Profile,
    int? RequestsPerMinute,
    int? TokensPerMinute,
    QuotaSnapshot Quota,
    FaultOptions Faults,
    bool FaultsOverridden);

/// <summary>
/// Resolves a deployment or model name to its profile, effective faults and quota state.
/// Unknown names fall back to the default profile with no quota and no faults, unless a fault
/// override has been set for that name at runtime.
/// </summary>
public sealed class DeploymentCatalog
{
    private readonly LlmMockOptions _options;
    private readonly TimeProvider _time;
    private readonly Dictionary<string, LatencyProfile> _profiles;
    private readonly DeploymentQuota _unlimited;
    private readonly ConcurrentDictionary<string, FaultOptions> _faultOverrides = new(StringComparer.OrdinalIgnoreCase);
    private volatile ConcurrentDictionary<string, DeploymentQuota> _quotas = NewQuotaMap();

    public DeploymentCatalog(LlmMockOptions options, TimeProvider time)
    {
        var errors = LlmMockOptionsValidator.Validate(options);
        if (errors.Count > 0)
        {
            throw new ArgumentException("Invalid LlmMock configuration: " + string.Join(" ", errors), nameof(options));
        }

        _options = options;
        _time = time;
        _unlimited = new DeploymentQuota(null, null, time);
        _profiles = options.Profiles.ToDictionary(
            p => p.Key,
            p => new LatencyProfile(p.Key, p.Value),
            StringComparer.OrdinalIgnoreCase);
    }

    public ResolvedDeployment Resolve(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var configured = _options.Deployments.TryGetValue(name, out var deployment);
        var profile = _profiles[configured ? deployment!.Profile : _options.DefaultProfile];
        var faults = _faultOverrides.TryGetValue(name, out var overridden)
            ? overridden
            : deployment?.Faults ?? FaultOptions.None;

        return new ResolvedDeployment(name, configured, profile, faults, QuotaFor(name, deployment));
    }

    public void SetFaultOverride(string name, FaultOptions faults)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var errors = LlmMockOptionsValidator.ValidateFaults(faults);
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors), nameof(faults));
        }

        _faultOverrides[name] = faults;
    }

    public bool ClearFaultOverride(string name) => _faultOverrides.TryRemove(name, out _);

    /// <summary>Clears all fault overrides and refills every quota, e.g. between test runs.</summary>
    public void Reset()
    {
        _faultOverrides.Clear();
        _quotas = NewQuotaMap();
    }

    /// <summary>Configured deployments plus any name that currently has a fault override.</summary>
    public IReadOnlyList<DeploymentState> Describe()
    {
        var names = _options.Deployments.Keys
            .Concat(_faultOverrides.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase);

        return names.Select(name =>
        {
            var resolved = Resolve(name);
            _options.Deployments.TryGetValue(name, out var deployment);
            return new DeploymentState(
                name,
                resolved.IsConfigured,
                resolved.Profile.Name,
                deployment?.RequestsPerMinute,
                deployment?.TokensPerMinute,
                resolved.Quota.Peek(),
                resolved.Faults,
                _faultOverrides.ContainsKey(name));
        }).ToList();
    }

    // Unknown names share one unlimited quota so arbitrary model names cannot grow the map.
    private DeploymentQuota QuotaFor(string name, DeploymentOptions? deployment) => deployment is null
        ? _unlimited
        : _quotas.GetOrAdd(name, _ => new DeploymentQuota(deployment.RequestsPerMinute, deployment.TokensPerMinute, _time));

    private static ConcurrentDictionary<string, DeploymentQuota> NewQuotaMap() => new(StringComparer.OrdinalIgnoreCase);
}
