using LlmMockService.Core.Configuration;

namespace LlmMockService.Core.Faults;

public enum RequestFault
{
    None,
    ServerError,
    ServiceUnavailable,
    RateLimited,
    ContentFilter,
    Timeout,
}

public readonly record struct StreamFaults(int? DisconnectAfterTokens, int? StallAtToken, TimeSpan StallDuration)
{
    public static StreamFaults None => default;
}

public static class FaultInjector
{
    /// <summary>Draws at most one request-level fault from mutually exclusive rates.</summary>
    public static RequestFault DecideRequestFault(FaultOptions faults, Random random)
    {
        if (faults.RequestFaultRateTotal <= 0)
        {
            return RequestFault.None;
        }

        var roll = random.NextDouble();
        var threshold = 0.0;
        foreach (var (kind, rate) in Rates(faults))
        {
            threshold += rate;
            if (roll < threshold)
            {
                return kind;
            }
        }

        return RequestFault.None;
    }

    /// <summary>
    /// Draws stream-level faults independently. A disconnect may happen before any token is sent;
    /// a stall always happens after the first token so it shows up as inter-token latency, not TTFT.
    /// </summary>
    public static StreamFaults DecideStreamFaults(FaultOptions faults, int outputTokens, Random random)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(outputTokens, 1);

        int? disconnectAfter = faults.MidStreamDisconnectRate > 0 && random.NextDouble() < faults.MidStreamDisconnectRate
            ? random.Next(0, outputTokens)
            : null;

        int? stallAt = outputTokens > 1 && faults.StallRate > 0 && random.NextDouble() < faults.StallRate
            ? random.Next(1, outputTokens)
            : null;

        return new StreamFaults(disconnectAfter, stallAt, TimeSpan.FromMilliseconds(stallAt is null ? 0 : faults.StallMilliseconds));
    }

    private static IEnumerable<(RequestFault Kind, double Rate)> Rates(FaultOptions faults)
    {
        yield return (RequestFault.ServerError, faults.ServerErrorRate);
        yield return (RequestFault.ServiceUnavailable, faults.ServiceUnavailableRate);
        yield return (RequestFault.RateLimited, faults.RateLimitRate);
        yield return (RequestFault.ContentFilter, faults.ContentFilterRate);
        yield return (RequestFault.Timeout, faults.TimeoutRate);
    }
}
