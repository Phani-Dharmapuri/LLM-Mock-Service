using LlmMockService.Core.Configuration;
using LlmMockService.Core.Faults;

namespace LlmMockService.Core.Latency;

/// <summary>Samples a <see cref="ResponsePlan"/> from a profile, applying any stream-level faults.</summary>
public sealed class ResponsePlanner(Random random)
{
    public ResponsePlan Create(LatencyProfile profile, FaultOptions faults, int inputTokens, int? requestedMaxOutputTokens)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(inputTokens);
        if (requestedMaxOutputTokens is { } max)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(max, 1, nameof(requestedMaxOutputTokens));
        }

        var natural = Math.Clamp((int)Math.Round(profile.OutputTokens.Sample(random)), 1, profile.MaxOutputTokens);
        var outputTokens = requestedMaxOutputTokens is { } cap ? Math.Min(natural, cap) : natural;
        var finishReason = outputTokens < natural ? FinishReason.Length : FinishReason.Stop;

        var ttftMs = profile.TimeToFirstTokenMs.Sample(random)
            + (profile.PrefillMsPer1kInputTokens * inputTokens / 1000.0);
        var meanItlMs = profile.InterTokenLatencyMs.Sample(random);

        var dueAt = new TimeSpan[outputTokens];
        var cursorMs = ttftMs;
        dueAt[0] = TimeSpan.FromMilliseconds(cursorMs);
        for (var i = 1; i < outputTokens; i++)
        {
            var jitter = 1 + (profile.InterTokenJitter * ((2 * random.NextDouble()) - 1));
            cursorMs += meanItlMs * jitter;
            dueAt[i] = TimeSpan.FromMilliseconds(cursorMs);
        }

        var streamFaults = FaultInjector.DecideStreamFaults(faults, outputTokens, random);
        if (streamFaults.StallAtToken is { } stallAt)
        {
            for (var i = stallAt; i < outputTokens; i++)
            {
                dueAt[i] += streamFaults.StallDuration;
            }
        }

        return new ResponsePlan(inputTokens, dueAt, finishReason, streamFaults.DisconnectAfterTokens);
    }
}
