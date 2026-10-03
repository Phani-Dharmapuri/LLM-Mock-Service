using LlmMockService.Core.Latency;

namespace LlmMockService.Core.Tests.Latency;

public class LogNormalDistributionTests
{
    [Fact]
    public void Samples_match_configured_median_and_p99()
    {
        var distribution = new LogNormalDistribution(median: 450, p99: 1800);
        var random = new Random(42);

        var samples = Enumerable.Range(0, 200_000).Select(_ => distribution.Sample(random)).Order().ToArray();

        Assert.InRange(Percentile(samples, 0.50), 450 * 0.98, 450 * 1.02);
        Assert.InRange(Percentile(samples, 0.99), 1800 * 0.95, 1800 * 1.05);
        Assert.All(samples, s => Assert.True(s > 0));
    }

    [Fact]
    public void Equal_median_and_p99_is_constant()
    {
        var distribution = new LogNormalDistribution(20, 20);
        var random = new Random(1);

        Assert.All(Enumerable.Range(0, 100), _ => Assert.Equal(20, distribution.Sample(random)));
    }

    [Fact]
    public void Zero_is_constant_zero()
    {
        var distribution = new LogNormalDistribution(0, 0);

        Assert.Equal(0, distribution.Sample(new Random(1)));
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(10, 5)]
    [InlineData(0, 5)]
    public void Invalid_parameters_throw(double median, double p99) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new LogNormalDistribution(median, p99));

    private static double Percentile(double[] sorted, double p) => sorted[(int)Math.Floor(p * (sorted.Length - 1))];
}
