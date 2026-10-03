using LlmMockService.Core.Configuration;

namespace LlmMockService.Core.Latency;

/// <summary>Immutable, ready-to-sample form of <see cref="LatencyProfileOptions"/>.</summary>
public sealed class LatencyProfile
{
    public LatencyProfile(string name, LatencyProfileOptions options)
    {
        Name = name;
        ModelName = options.ModelName;
        Encoding = options.Encoding;
        TimeToFirstTokenMs = LogNormalDistribution.From(options.TimeToFirstTokenMs);
        PrefillMsPer1kInputTokens = options.PrefillMsPer1kInputTokens;
        InterTokenLatencyMs = LogNormalDistribution.From(options.InterTokenLatencyMs);
        InterTokenJitter = options.InterTokenJitter;
        OutputTokens = LogNormalDistribution.From(options.OutputTokens);
        MaxOutputTokens = options.MaxOutputTokens;
    }

    public string Name { get; }
    public string? ModelName { get; }
    public string Encoding { get; }
    public LogNormalDistribution TimeToFirstTokenMs { get; }
    public double PrefillMsPer1kInputTokens { get; }
    public LogNormalDistribution InterTokenLatencyMs { get; }
    public double InterTokenJitter { get; }
    public LogNormalDistribution OutputTokens { get; }
    public int MaxOutputTokens { get; }

    /// <summary>Output tokens to reserve against a TPM quota when the client does not send a max.</summary>
    public int EstimatedOutputTokens => (int)Math.Clamp(Math.Round(OutputTokens.Median), 1, MaxOutputTokens);
}
