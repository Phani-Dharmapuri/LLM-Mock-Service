using LlmMockService.Core.Configuration;
using LlmMockService.Core.Faults;

namespace LlmMockService.Core.Tests.Faults;

public class FaultInjectorTests
{
    [Fact]
    public void No_rates_means_no_fault()
    {
        var random = new Random(3);

        Assert.All(Enumerable.Range(0, 1000), _ => Assert.Equal(RequestFault.None, FaultInjector.DecideRequestFault(FaultOptions.None, random)));
    }

    [Theory]
    [InlineData(nameof(FaultOptions.ServerErrorRate), RequestFault.ServerError)]
    [InlineData(nameof(FaultOptions.ServiceUnavailableRate), RequestFault.ServiceUnavailable)]
    [InlineData(nameof(FaultOptions.RateLimitRate), RequestFault.RateLimited)]
    [InlineData(nameof(FaultOptions.ContentFilterRate), RequestFault.ContentFilter)]
    [InlineData(nameof(FaultOptions.TimeoutRate), RequestFault.Timeout)]
    public void Rate_of_one_always_injects_that_fault(string property, RequestFault expected)
    {
        var faults = new FaultOptions();
        typeof(FaultOptions).GetProperty(property)!.SetValue(faults, 1.0);

        Assert.Equal(expected, FaultInjector.DecideRequestFault(faults, new Random(5)));
    }

    [Fact]
    public void Mixed_rates_are_drawn_in_proportion()
    {
        var faults = new FaultOptions { ServerErrorRate = 0.1, ServiceUnavailableRate = 0.2 };
        var random = new Random(11);
        const int draws = 100_000;

        var counts = Enumerable.Range(0, draws)
            .Select(_ => FaultInjector.DecideRequestFault(faults, random))
            .CountBy(f => f)
            .ToDictionary();

        Assert.InRange(counts[RequestFault.ServerError] / (double)draws, 0.095, 0.105);
        Assert.InRange(counts[RequestFault.ServiceUnavailable] / (double)draws, 0.195, 0.205);
        Assert.InRange(counts[RequestFault.None] / (double)draws, 0.69, 0.71);
    }

    [Fact]
    public void Stream_faults_are_none_without_rates()
    {
        Assert.Equal(StreamFaults.None, FaultInjector.DecideStreamFaults(FaultOptions.None, 100, new Random(1)));
    }

    [Fact]
    public void Stall_needs_at_least_two_tokens_so_ttft_is_untouched()
    {
        var faults = new FaultOptions { StallRate = 1 };

        Assert.Null(FaultInjector.DecideStreamFaults(faults, 1, new Random(1)).StallAtToken);
    }

    [Fact]
    public void Stall_and_disconnect_indices_are_within_output()
    {
        var faults = new FaultOptions { StallRate = 1, StallMilliseconds = 250, MidStreamDisconnectRate = 1 };
        var random = new Random(9);

        for (var i = 0; i < 1000; i++)
        {
            var decision = FaultInjector.DecideStreamFaults(faults, 10, random);
            Assert.InRange(decision.StallAtToken!.Value, 1, 9);
            Assert.InRange(decision.DisconnectAfterTokens!.Value, 0, 9);
            Assert.Equal(TimeSpan.FromMilliseconds(250), decision.StallDuration);
        }
    }
}
