using LlmMockService.Core.Configuration;

namespace LlmMockService.Core.Tests.Configuration;

public class LlmMockOptionsValidatorTests
{
    [Fact]
    public void Valid_options_have_no_errors() => Assert.Empty(LlmMockOptionsValidator.Validate(TestOptions.Valid()));

    [Fact]
    public void Missing_default_profile_is_reported()
    {
        var options = TestOptions.Valid();
        options.DefaultProfile = "nope";

        Assert.Contains(LlmMockOptionsValidator.Validate(options), e => e.Contains("DefaultProfile 'nope'", StringComparison.Ordinal));
    }

    [Fact]
    public void Deployment_with_unknown_profile_is_reported()
    {
        var options = TestOptions.Valid();
        options.Deployments["d"] = new DeploymentOptions { Profile = "ghost" };

        Assert.Contains(LlmMockOptionsValidator.Validate(options), e => e.Contains("Deployments:d", StringComparison.Ordinal));
    }

    [Fact]
    public void Unsupported_encoding_is_reported()
    {
        var options = TestOptions.Valid();
        options.Profiles["default"].Encoding = "p50k_base";

        Assert.Contains(LlmMockOptionsValidator.Validate(options), e => e.Contains("p50k_base", StringComparison.Ordinal));
    }

    [Fact]
    public void P99_below_median_is_reported()
    {
        var options = TestOptions.Valid();
        options.Profiles["default"].TimeToFirstTokenMs = new DistributionOptions { Median = 500, P99 = 100 };

        Assert.Contains(LlmMockOptionsValidator.Validate(options), e => e.Contains("TimeToFirstTokenMs", StringComparison.Ordinal));
    }

    [Fact]
    public void Request_fault_rates_summing_above_one_are_reported()
    {
        var faults = new FaultOptions { ServerErrorRate = 0.6, ServiceUnavailableRate = 0.6 };

        Assert.Contains(LlmMockOptionsValidator.ValidateFaults(faults), e => e.Contains("sum to at most 1", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    public void Fault_rate_outside_unit_interval_is_reported(double rate)
    {
        var faults = new FaultOptions { StallRate = rate };

        Assert.Contains(LlmMockOptionsValidator.ValidateFaults(faults), e => e.Contains("StallRate", StringComparison.Ordinal));
    }
}
