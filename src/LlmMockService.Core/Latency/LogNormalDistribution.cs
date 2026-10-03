using LlmMockService.Core.Configuration;

namespace LlmMockService.Core.Latency;

/// <summary>
/// Log-normal distribution parameterised by median and p99. LLM latencies are right-skewed with a long
/// tail, which a log-normal captures with two numbers teams already track.
/// </summary>
public sealed class LogNormalDistribution
{
    /// <summary>z-score of the 99th percentile of the standard normal distribution.</summary>
    private const double Z99 = 2.3263478740408408;

    private readonly double _mu;
    private readonly double _sigma;
    private readonly bool _isZero;

    public LogNormalDistribution(double median, double p99)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(median);
        ArgumentOutOfRangeException.ThrowIfLessThan(p99, median);
        if (median == 0 && p99 > 0)
        {
            throw new ArgumentOutOfRangeException(nameof(median), "Median must be positive when P99 is positive.");
        }

        Median = median;
        P99 = p99;
        _isZero = median == 0;
        _mu = _isZero ? 0 : Math.Log(median);
        _sigma = _isZero ? 0 : (Math.Log(p99) - _mu) / Z99;
    }

    public double Median { get; }
    public double P99 { get; }

    public static LogNormalDistribution From(DistributionOptions options) => new(options.Median, options.P99);

    public double Sample(Random random)
    {
        if (_isZero)
        {
            return 0;
        }

        return _sigma == 0 ? Median : Math.Exp(_mu + (_sigma * StandardNormal(random)));
    }

    /// <summary>Box-Muller transform; one value per call keeps the type stateless and thread-safe.</summary>
    private static double StandardNormal(Random random)
    {
        var u1 = 1.0 - random.NextDouble(); // (0, 1] so Log never sees 0
        var u2 = random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}
