using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LlmMockService.Server.Tests;

public class AdminApiTests(MockServerFactory factory) : IClassFixture<MockServerFactory>
{
    [Fact]
    public async Task Reset_clears_overrides_and_refills_quotas()
    {
        var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;
        await factory.SetFaultsAsync("to-be-reset", new { serverErrorRate = 1.0 });

        using var reset = await client.PostAsync("/_admin/reset", content: null, ct);
        var deployments = await client.GetFromJsonAsync<JsonElement>("/_admin/deployments", ct);

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        var names = deployments.EnumerateArray().Select(d => d.GetProperty("name").GetString()).ToArray();
        Assert.DoesNotContain("to-be-reset", names);
        Assert.Contains(MockServerFactory.TwoRpm, names);
    }

    [Fact]
    public async Task Deployments_report_quota_and_effective_faults()
    {
        var ct = TestContext.Current.CancellationToken;
        await factory.SetFaultsAsync("observed", new { stallRate = 0.25, stallMilliseconds = 100 });

        var deployments = await factory.CreateClient().GetFromJsonAsync<JsonElement>("/_admin/deployments", ct);

        var observed = deployments.EnumerateArray().Single(d => d.GetProperty("name").GetString() == "observed");
        Assert.True(observed.GetProperty("faultsOverridden").GetBoolean());
        Assert.False(observed.GetProperty("isConfigured").GetBoolean());
        Assert.Equal(0.25, observed.GetProperty("faults").GetProperty("stallRate").GetDouble());

        var limited = deployments.EnumerateArray().Single(d => d.GetProperty("name").GetString() == MockServerFactory.TwoRpm);
        Assert.Equal(2, limited.GetProperty("quota").GetProperty("requestLimit").GetInt32());
    }

    [Fact]
    public async Task Health_endpoint_responds()
    {
        using var response = await factory.CreateClient().GetAsync("/healthz", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
