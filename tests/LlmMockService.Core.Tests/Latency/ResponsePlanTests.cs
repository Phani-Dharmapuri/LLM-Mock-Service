using LlmMockService.Core.Latency;

namespace LlmMockService.Core.Tests.Latency;

public class ResponsePlanTests
{
    private static readonly TimeSpan[] Schedule = [Ms(100), Ms(120), Ms(120), Ms(140)];

    [Theory]
    [InlineData(0, 0)]
    [InlineData(99, 0)]
    [InlineData(100, 1)]
    [InlineData(119, 1)]
    [InlineData(120, 3)] // two tokens share a due time; both count
    [InlineData(139, 3)]
    [InlineData(140, 4)]
    [InlineData(10_000, 4)]
    public void TokensDueBy_counts_tokens_due_at_or_before_elapsed(int elapsedMs, int expected)
    {
        var plan = new ResponsePlan(10, Schedule, FinishReason.Stop);

        Assert.Equal(expected, plan.TokensDueBy(Ms(elapsedMs)));
    }

    [Fact]
    public void Exposes_ttft_and_total_duration()
    {
        var plan = new ResponsePlan(10, Schedule, FinishReason.Stop);

        Assert.Equal(Ms(100), plan.TimeToFirstToken);
        Assert.Equal(Ms(140), plan.TotalDuration);
        Assert.Equal(4, plan.OutputTokens);
    }

    [Fact]
    public void Rejects_decreasing_schedule() =>
        Assert.Throws<ArgumentException>(() => new ResponsePlan(1, [Ms(20), Ms(10)], FinishReason.Stop));

    [Fact]
    public void Rejects_disconnect_index_outside_output() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResponsePlan(1, [Ms(1), Ms(2)], FinishReason.Stop, disconnectAfterTokens: 2));

    private static TimeSpan Ms(int value) => TimeSpan.FromMilliseconds(value);
}
