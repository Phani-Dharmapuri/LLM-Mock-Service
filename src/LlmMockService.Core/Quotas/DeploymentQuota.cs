namespace LlmMockService.Core.Quotas;

public readonly record struct QuotaSnapshot(int? RequestLimit, int RemainingRequests, int? TokenLimit, int RemainingTokens);

public readonly record struct QuotaDecision(bool Allowed, TimeSpan RetryAfter, QuotaSnapshot Snapshot);

/// <summary>
/// Per-deployment RPM and TPM quota as two continuously refilling token buckets, acquired atomically.
/// </summary>
/// <remarks>
/// A custom type rather than System.Threading.RateLimiting because a request must take from both buckets
/// or neither, and the 429 needs an exact retry-after for the bucket that is short. Like Azure OpenAI,
/// tokens are charged up front (prompt + requested max output) and not refunded.
/// </remarks>
public sealed class DeploymentQuota
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly TimeProvider _time;
    private readonly Bucket? _requests;
    private readonly Bucket? _tokens;
    private readonly Lock _gate = new();

    public DeploymentQuota(int? requestsPerMinute, int? tokensPerMinute, TimeProvider time)
    {
        _time = time;
        var now = time.GetTimestamp();
        _requests = requestsPerMinute is > 0 ? new Bucket(requestsPerMinute.Value, now) : null;
        _tokens = tokensPerMinute is > 0 ? new Bucket(tokensPerMinute.Value, now) : null;
    }

    public bool IsUnlimited => _requests is null && _tokens is null;

    public QuotaDecision TryAcquire(int tokens)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tokens);

        lock (_gate)
        {
            var now = _time.GetTimestamp();
            _requests?.Refill(now, _time);
            _tokens?.Refill(now, _time);

            var retryAfter = TimeSpan.Zero;
            if (_requests is not null)
            {
                retryAfter = Max(retryAfter, _requests.TimeUntilAvailable(1));
            }

            if (_tokens is not null)
            {
                retryAfter = Max(retryAfter, _tokens.TimeUntilAvailable(tokens));
            }

            var allowed = retryAfter == TimeSpan.Zero;
            if (allowed)
            {
                _requests?.Take(1);
                _tokens?.Take(tokens);
            }

            return new QuotaDecision(allowed, retryAfter, Snapshot());
        }
    }

    /// <summary>Current remaining quota, without consuming any.</summary>
    public QuotaSnapshot Peek()
    {
        lock (_gate)
        {
            var now = _time.GetTimestamp();
            _requests?.Refill(now, _time);
            _tokens?.Refill(now, _time);
            return Snapshot();
        }
    }

    private QuotaSnapshot Snapshot() => new(
        _requests?.Capacity,
        (int)Math.Floor(_requests?.Available ?? 0),
        _tokens?.Capacity,
        (int)Math.Floor(_tokens?.Available ?? 0));

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    private sealed class Bucket(int capacity, long createdAt)
    {
        private long _lastRefill = createdAt;

        public int Capacity { get; } = capacity;
        public double Available { get; private set; } = capacity;

        private double PerSecond => Capacity / Window.TotalSeconds;

        public void Refill(long now, TimeProvider time)
        {
            var elapsed = time.GetElapsedTime(_lastRefill, now);
            Available = Math.Min(Capacity, Available + (elapsed.TotalSeconds * PerSecond));
            _lastRefill = now;
        }

        /// <summary>Zero if available now; a full window if the request can never fit.</summary>
        public TimeSpan TimeUntilAvailable(int amount)
        {
            if (amount > Capacity)
            {
                return Window;
            }

            var deficit = amount - Available;
            return deficit <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(deficit / PerSecond);
        }

        public void Take(int amount) => Available -= amount;
    }
}
