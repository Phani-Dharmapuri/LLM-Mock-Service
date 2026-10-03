using System.ClientModel;
using System.Net;
using System.Text.Json;
using OpenAI.Chat;

namespace LlmMockService.Server.Tests;

public class ErrorAndFaultTests(MockServerFactory factory) : IClassFixture<MockServerFactory>
{
    private const string MinimalBody = """{"model":"MODEL","messages":[{"role":"user","content":"hi"}]}""";

    [Fact]
    public async Task Quota_exhaustion_returns_429_with_retry_headers()
    {
        var path = $"/openai/deployments/{MockServerFactory.TwoRpm}/chat/completions?api-version=2024-10-21";
        const string body = """{"messages":[{"role":"user","content":"hi"}],"max_tokens":5}""";

        using var first = await factory.PostChatAsync(body, path);
        using var second = await factory.PostChatAsync(body, path);
        using var third = await factory.PostChatAsync(body, path);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("2", Header(first, "x-ratelimit-limit-requests"));
        Assert.Equal("1", Header(first, "x-ratelimit-remaining-requests"));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Equal("30", Header(third, "Retry-After")); // 2 per minute refills one every 30 s
        Assert.InRange(int.Parse(Header(third, "retry-after-ms"), System.Globalization.CultureInfo.InvariantCulture), 29_000, 30_000);
        Assert.Equal("rate_limit_exceeded", await ErrorCodeAsync(third));
    }

    [Fact]
    public async Task Sdk_surfaces_injected_503_as_client_result_exception()
    {
        await factory.SetFaultsAsync("outage-deployment", new { serviceUnavailableRate = 1.0 });
        var client = factory.AzureChatClient("outage-deployment");

        var ex = await Assert.ThrowsAsync<ClientResultException>(() =>
            client.CompleteChatAsync([new UserChatMessage("hi")], cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(503, ex.Status);
    }

    [Fact]
    public async Task Clearing_a_fault_override_restores_normal_responses()
    {
        await factory.SetFaultsAsync("flaky-model", new { serverErrorRate = 1.0 });
        using var failing = await factory.PostChatAsync(MinimalBody.Replace("MODEL", "flaky-model", StringComparison.Ordinal));

        using var deleted = await factory.CreateClient().DeleteAsync("/_admin/deployments/flaky-model/faults", TestContext.Current.CancellationToken);
        using var healthy = await factory.PostChatAsync(MinimalBody.Replace("MODEL", "flaky-model", StringComparison.Ordinal));

        Assert.Equal(HttpStatusCode.InternalServerError, failing.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.OK, healthy.StatusCode);
    }

    [Theory]
    [InlineData("contentFilterRate", HttpStatusCode.BadRequest, "content_filter")]
    [InlineData("rateLimitRate", HttpStatusCode.TooManyRequests, "rate_limit_exceeded")]
    public async Task Injected_faults_use_provider_error_shapes(string fault, HttpStatusCode status, string code)
    {
        var deployment = $"fault-{fault}";
        await factory.SetFaultsAsync(deployment, new Dictionary<string, double> { [fault] = 1.0 });

        using var response = await factory.PostChatAsync(MinimalBody.Replace("MODEL", deployment, StringComparison.Ordinal));

        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Timeout_fault_holds_the_request_then_returns_504()
    {
        await factory.SetFaultsAsync("slow-to-fail", new { timeoutRate = 1.0, timeoutMilliseconds = 200 });
        var started = System.Diagnostics.Stopwatch.StartNew();

        using var response = await factory.PostChatAsync(MinimalBody.Replace("MODEL", "slow-to-fail", StringComparison.Ordinal));

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.True(started.ElapsedMilliseconds >= 200, $"Returned after {started.ElapsedMilliseconds} ms.");
    }

    [Fact]
    public async Task Mid_stream_disconnect_breaks_the_client_stream()
    {
        await factory.SetFaultsAsync("dropping-deployment", new { midStreamDisconnectRate = 1.0 });
        var client = factory.OpenAiChatClient("dropping-deployment");

        var ex = await Record.ExceptionAsync(async () =>
        {
            await foreach (var _ in client.CompleteChatStreamingAsync([new UserChatMessage("hi")], cancellationToken: TestContext.Current.CancellationToken))
            {
            }
        });

        Assert.NotNull(ex);
        Assert.False(ex is OperationCanceledException, $"Expected a broken connection, got cancellation: {ex}");
    }

    [Fact]
    public async Task Invalid_fault_override_is_rejected()
    {
        var response = await factory.CreateClient().PutAsync(
            "/_admin/deployments/x/faults",
            new StringContent("""{"serverErrorRate":2}""", System.Text.Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("""{"model":""", "Request body is not valid JSON")]
    [InlineData("""{"model":"m"}""", "'messages' must be a non-empty array.")]
    [InlineData("""{"model":"m","messages":[]}""", "'messages' must be a non-empty array.")]
    [InlineData("""{"messages":[{"role":"user","content":"hi"}]}""", "'model' is required.")]
    [InlineData("""{"model":"m","messages":[{"role":"user","content":"hi"}],"max_tokens":0}""", "max_tokens must be at least 1.")]
    public async Task Invalid_requests_return_openai_style_400(string body, string expectedMessage)
    {
        using var response = await factory.PostChatAsync(body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var error = json.RootElement.GetProperty("error");
        Assert.StartsWith(expectedMessage, error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal("invalid_request_error", error.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Content_part_arrays_are_accepted()
    {
        const string body = """{"model":"m","messages":[{"role":"user","content":[{"type":"text","text":"hello"},{"type":"image_url","image_url":{"url":"x"}}]}]}""";

        using var response = await factory.PostChatAsync(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.Single() : throw new Xunit.Sdk.XunitException($"Missing header {name}.");

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return json.RootElement.GetProperty("error").GetProperty("code").GetString();
    }
}
