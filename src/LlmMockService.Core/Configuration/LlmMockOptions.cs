namespace LlmMockService.Core.Configuration;

/// <summary>Root configuration, bound from the <c>LlmMock</c> section.</summary>
public sealed class LlmMockOptions
{
    public const string SectionName = "LlmMock";

    /// <summary>Profile used for any deployment/model name that is not listed in <see cref="Deployments"/>.</summary>
    public string DefaultProfile { get; set; } = "default";

    /// <summary>Latency and output-length profiles, keyed by profile name.</summary>
    public Dictionary<string, LatencyProfileOptions> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Deployments keyed by name. The key matches the Azure OpenAI deployment segment
    /// (<c>/openai/deployments/{name}/...</c>) or the OpenAI <c>model</c> field.
    /// </summary>
    public Dictionary<string, DeploymentOptions> Deployments { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Minimum wait between stream writes after the first token. Tokens that fall due inside one tick are
    /// written together, which keeps timer overhead flat at high concurrency without accumulating drift.
    /// 25 ms keeps ~1,000 concurrent streams on schedule per instance (see README, "Capacity and accuracy").
    /// </summary>
    public int StreamTickMilliseconds { get; set; } = 25;

    /// <summary>Expose the <c>/_admin</c> runtime control API.</summary>
    public bool AdminApiEnabled { get; set; } = true;
}

public sealed class LatencyProfileOptions
{
    /// <summary>Value returned in the response <c>model</c> field. Defaults to the requested deployment/model name.</summary>
    public string? ModelName { get; set; }

    /// <summary>Tiktoken encoding used for token accounting: <c>o200k_base</c> or <c>cl100k_base</c>.</summary>
    public string Encoding { get; set; } = "o200k_base";

    /// <summary>Base time to first token, excluding prompt-size dependent prefill.</summary>
    public DistributionOptions TimeToFirstTokenMs { get; set; } = new();

    /// <summary>Additional time to first token per 1,000 prompt tokens (prefill cost).</summary>
    public double PrefillMsPer1kInputTokens { get; set; }

    /// <summary>Mean inter-token latency of a single response, sampled once per request.</summary>
    public DistributionOptions InterTokenLatencyMs { get; set; } = new();

    /// <summary>Uniform per-token jitter around the request's mean inter-token latency, as a fraction (0..1).</summary>
    public double InterTokenJitter { get; set; } = 0.2;

    /// <summary>Number of completion tokens generated when the client does not cap it lower.</summary>
    public DistributionOptions OutputTokens { get; set; } = new();

    /// <summary>Hard cap on generated tokens, independent of the sampled distribution.</summary>
    public int MaxOutputTokens { get; set; } = 4096;
}

/// <summary>
/// A log-normal distribution described by its median and 99th percentile, the two figures most
/// observability stacks already report. Equal values give a constant; zero for both gives zero.
/// </summary>
public sealed class DistributionOptions
{
    public double Median { get; set; }
    public double P99 { get; set; }
}

public sealed class DeploymentOptions
{
    public string Profile { get; set; } = "";

    /// <summary>Requests-per-minute quota. Null or 0 means unlimited.</summary>
    public int? RequestsPerMinute { get; set; }

    /// <summary>Tokens-per-minute quota (prompt tokens + requested max output tokens). Null or 0 means unlimited.</summary>
    public int? TokensPerMinute { get; set; }

    public FaultOptions Faults { get; set; } = new();
}

/// <summary>
/// Fault-injection rates, each a probability in 0..1. The request-level rates are mutually exclusive
/// and must sum to at most 1; stream-level rates are drawn independently.
/// </summary>
public sealed class FaultOptions
{
    public double ServerErrorRate { get; set; }
    public double ServiceUnavailableRate { get; set; }
    public double RateLimitRate { get; set; }
    public double ContentFilterRate { get; set; }
    public double TimeoutRate { get; set; }

    /// <summary>How long a "timeout" fault holds the request before answering 504.</summary>
    public int TimeoutMilliseconds { get; set; } = 120_000;

    public double MidStreamDisconnectRate { get; set; }
    public double StallRate { get; set; }
    public int StallMilliseconds { get; set; } = 5_000;

    public static FaultOptions None { get; } = new();

    internal double RequestFaultRateTotal =>
        ServerErrorRate + ServiceUnavailableRate + RateLimitRate + ContentFilterRate + TimeoutRate;
}
