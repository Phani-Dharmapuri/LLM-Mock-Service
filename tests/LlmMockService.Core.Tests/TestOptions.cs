using LlmMockService.Core.Configuration;

namespace LlmMockService.Core.Tests;

internal static class TestOptions
{
    public static LlmMockOptions Valid()
    {
        var options = new LlmMockOptions { DefaultProfile = "default" };
        options.Profiles["default"] = new LatencyProfileOptions
        {
            TimeToFirstTokenMs = new() { Median = 100, P99 = 400 },
            InterTokenLatencyMs = new() { Median = 10, P99 = 30 },
            OutputTokens = new() { Median = 50, P99 = 200 },
        };
        options.Deployments["limited"] = new DeploymentOptions { Profile = "default", RequestsPerMinute = 2, TokensPerMinute = 10_000 };
        return options;
    }
}
