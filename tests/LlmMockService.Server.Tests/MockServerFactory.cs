using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net.Http.Json;
using System.Text;
using Azure.AI.OpenAI;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using OpenAI;
using OpenAI.Chat;

namespace LlmMockService.Server.Tests;

/// <summary>
/// Hosts the mock in memory with deterministic test profiles. Tests use their own deployment names,
/// so the illustrative profiles and faults in appsettings.json never affect them.
/// </summary>
public sealed class MockServerFactory : WebApplicationFactory<Program>
{
    public const string Instant = "instant";
    public const int InstantOutputTokens = 12;
    public const string Slow = "slow-deployment";
    public const int SlowTtftMs = 300;
    public const int SlowItlMs = 20;
    public const int SlowOutputTokens = 10;
    public const string TwoRpm = "two-rpm-deployment";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LlmMock:DefaultProfile"] = "instant",
            ["LlmMock:Profiles:instant:Encoding"] = "o200k_base",
            ["LlmMock:Profiles:instant:TimeToFirstTokenMs:Median"] = "0",
            ["LlmMock:Profiles:instant:TimeToFirstTokenMs:P99"] = "0",
            ["LlmMock:Profiles:instant:InterTokenLatencyMs:Median"] = "0",
            ["LlmMock:Profiles:instant:InterTokenLatencyMs:P99"] = "0",
            ["LlmMock:Profiles:instant:OutputTokens:Median"] = $"{InstantOutputTokens}",
            ["LlmMock:Profiles:instant:OutputTokens:P99"] = $"{InstantOutputTokens}",

            ["LlmMock:Profiles:slow:Encoding"] = "o200k_base",
            ["LlmMock:Profiles:slow:TimeToFirstTokenMs:Median"] = $"{SlowTtftMs}",
            ["LlmMock:Profiles:slow:TimeToFirstTokenMs:P99"] = $"{SlowTtftMs}",
            ["LlmMock:Profiles:slow:InterTokenLatencyMs:Median"] = $"{SlowItlMs}",
            ["LlmMock:Profiles:slow:InterTokenLatencyMs:P99"] = $"{SlowItlMs}",
            ["LlmMock:Profiles:slow:InterTokenJitter"] = "0",
            ["LlmMock:Profiles:slow:OutputTokens:Median"] = $"{SlowOutputTokens}",
            ["LlmMock:Profiles:slow:OutputTokens:P99"] = $"{SlowOutputTokens}",

            [$"LlmMock:Deployments:{Slow}:Profile"] = "slow",
            [$"LlmMock:Deployments:{TwoRpm}:Profile"] = "instant",
            [$"LlmMock:Deployments:{TwoRpm}:RequestsPerMinute"] = "2",
            [$"LlmMock:Deployments:{TwoRpm}:TokensPerMinute"] = "100000",
        }));
    }

    public ChatClient OpenAiChatClient(string model) => new(model, new ApiKeyCredential("test-key"), new OpenAIClientOptions
    {
        Endpoint = new Uri(Server.BaseAddress, "v1"),
        Transport = new HttpClientPipelineTransport(CreateClient()),
        RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
    });

    public ChatClient AzureChatClient(string deployment) => new AzureOpenAIClient(Server.BaseAddress, new ApiKeyCredential("test-key"), new AzureOpenAIClientOptions
    {
        Transport = new HttpClientPipelineTransport(CreateClient()),
        RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
    }).GetChatClient(deployment);

    public async Task<HttpResponseMessage> PostChatAsync(string json, string path = "/v1/chat/completions")
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await CreateClient().PostAsync(path, content, TestContext.Current.CancellationToken);
    }

    public async Task SetFaultsAsync(string deployment, object faults)
    {
        var response = await CreateClient().PutAsJsonAsync($"/_admin/deployments/{deployment}/faults", faults, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
