using LlmMockService.Core.Configuration;
using LlmMockService.Core.Latency;

namespace LlmMockService.Core.Tests.Latency;

public class ResponsePlannerTests
{
    private readonly ResponsePlanner _planner = new(new Random(7));

    [Fact]
    public void Constant_profile_produces_exact_schedule_including_prefill()
    {
        var profile = Profile(ttftMs: 100, prefillPer1k: 10, itlMs: 20, outputTokens: 50);

        var plan = _planner.Create(profile, FaultOptions.None, inputTokens: 1000, requestedMaxOutputTokens: null);

        Assert.Equal(50, plan.OutputTokens);
        Assert.Equal(1000, plan.InputTokens);
        Assert.Equal(FinishReason.Stop, plan.FinishReason);
        Assert.Equal(TimeSpan.FromMilliseconds(110), plan.TimeToFirstToken);
        Assert.Equal(TimeSpan.FromMilliseconds(110 + (49 * 20)), plan.TotalDuration);
        Assert.Null(plan.DisconnectAfterTokens);
    }

    [Fact]
    public void Requested_max_below_natural_length_truncates_with_length_finish_reason()
    {
        var plan = _planner.Create(Profile(outputTokens: 50), FaultOptions.None, 10, requestedMaxOutputTokens: 8);

        Assert.Equal(8, plan.OutputTokens);
        Assert.Equal(FinishReason.Length, plan.FinishReason);
    }

    [Fact]
    public void Requested_max_above_natural_length_stops_naturally()
    {
        var plan = _planner.Create(Profile(outputTokens: 50), FaultOptions.None, 10, requestedMaxOutputTokens: 500);

        Assert.Equal(50, plan.OutputTokens);
        Assert.Equal(FinishReason.Stop, plan.FinishReason);
    }

    [Fact]
    public void Output_is_capped_by_profile_max()
    {
        var options = ProfileOptions(outputTokens: 50);
        options.MaxOutputTokens = 16;

        var plan = _planner.Create(new LatencyProfile("p", options), FaultOptions.None, 10, null);

        Assert.Equal(16, plan.OutputTokens);
        Assert.Equal(FinishReason.Stop, plan.FinishReason);
    }

    [Fact]
    public void Stall_delays_later_tokens_but_not_the_first()
    {
        var profile = Profile(ttftMs: 100, itlMs: 10, outputTokens: 20);
        var faults = new FaultOptions { StallRate = 1, StallMilliseconds = 3000 };

        var plan = _planner.Create(profile, faults, 0, null);

        Assert.Equal(TimeSpan.FromMilliseconds(100), plan.TimeToFirstToken);
        Assert.Equal(TimeSpan.FromMilliseconds(100 + (19 * 10) + 3000), plan.TotalDuration);
    }

    [Fact]
    public void Disconnect_fault_sets_index_within_output()
    {
        var faults = new FaultOptions { MidStreamDisconnectRate = 1 };

        var plan = _planner.Create(Profile(outputTokens: 20), faults, 0, null);

        Assert.InRange(plan.DisconnectAfterTokens!.Value, 0, 19);
    }

    [Fact]
    public void Jitter_keeps_schedule_monotonic_and_mean_itl_on_target()
    {
        var options = ProfileOptions(ttftMs: 0, itlMs: 20, outputTokens: 2000);
        options.InterTokenJitter = 0.5;

        var plan = _planner.Create(new LatencyProfile("p", options), FaultOptions.None, 0, null);

        var meanItl = plan.TotalDuration.TotalMilliseconds / (plan.OutputTokens - 1);
        Assert.InRange(meanItl, 19.5, 20.5);
        for (var i = 1; i < plan.OutputTokens; i++)
        {
            Assert.True(plan.TokenDueAt(i) >= plan.TokenDueAt(i - 1));
        }
    }

    private static LatencyProfile Profile(double ttftMs = 0, double prefillPer1k = 0, double itlMs = 0, double outputTokens = 10) =>
        new("test", ProfileOptions(ttftMs, prefillPer1k, itlMs, outputTokens));

    private static LatencyProfileOptions ProfileOptions(double ttftMs = 0, double prefillPer1k = 0, double itlMs = 0, double outputTokens = 10) => new()
    {
        TimeToFirstTokenMs = new() { Median = ttftMs, P99 = ttftMs },
        PrefillMsPer1kInputTokens = prefillPer1k,
        InterTokenLatencyMs = new() { Median = itlMs, P99 = itlMs },
        InterTokenJitter = 0,
        OutputTokens = new() { Median = outputTokens, P99 = outputTokens },
    };
}
