namespace LlmMockService.Core.Latency;

public enum FinishReason
{
    Stop,
    Length,
}

/// <summary>
/// Pre-computed timeline of one response: when each output token is due, measured from request arrival.
/// Writers compare elapsed time against this schedule rather than sleeping per token, so timer jitter
/// never accumulates into drift.
/// </summary>
public sealed class ResponsePlan
{
    private readonly TimeSpan[] _tokenDueAt;

    public ResponsePlan(int inputTokens, TimeSpan[] tokenDueAt, FinishReason finishReason, int? disconnectAfterTokens = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(inputTokens);
        ArgumentOutOfRangeException.ThrowIfZero(tokenDueAt.Length);
        if (disconnectAfterTokens is { } d)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(d, nameof(disconnectAfterTokens));
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(d, tokenDueAt.Length, nameof(disconnectAfterTokens));
        }

        for (var i = 1; i < tokenDueAt.Length; i++)
        {
            if (tokenDueAt[i] < tokenDueAt[i - 1])
            {
                throw new ArgumentException("Token schedule must be non-decreasing.", nameof(tokenDueAt));
            }
        }

        InputTokens = inputTokens;
        _tokenDueAt = tokenDueAt;
        FinishReason = finishReason;
        DisconnectAfterTokens = disconnectAfterTokens;
    }

    public int InputTokens { get; }
    public int OutputTokens => _tokenDueAt.Length;
    public FinishReason FinishReason { get; }

    /// <summary>When set, the connection is dropped after this many tokens have been sent.</summary>
    public int? DisconnectAfterTokens { get; }

    public TimeSpan TimeToFirstToken => _tokenDueAt[0];
    public TimeSpan TotalDuration => _tokenDueAt[^1];

    public TimeSpan TokenDueAt(int index) => _tokenDueAt[index];

    /// <summary>Number of tokens whose due time is at or before <paramref name="elapsed"/>.</summary>
    public int TokensDueBy(TimeSpan elapsed)
    {
        var index = Array.BinarySearch(_tokenDueAt, elapsed);
        if (index < 0)
        {
            return ~index;
        }

        // Equal due times are possible (zero latency); count all of them.
        while (index + 1 < _tokenDueAt.Length && _tokenDueAt[index + 1] == elapsed)
        {
            index++;
        }

        return index + 1;
    }
}
