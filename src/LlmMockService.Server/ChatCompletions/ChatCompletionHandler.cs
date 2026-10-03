using System.Text;
using System.Text.Json;
using LlmMockService.Core.Deployments;
using LlmMockService.Core.Faults;
using LlmMockService.Core.Latency;
using LlmMockService.Core.Tokens;
using LlmMockService.Server.Protocol;

namespace LlmMockService.Server.ChatCompletions;

/// <summary>
/// Handles <c>POST /v1/chat/completions</c> (OpenAI) and
/// <c>POST /openai/deployments/{deployment}/chat/completions</c> (Azure OpenAI).
/// Order mirrors a real provider: validate, charge quota, maybe fail, then generate on a timed schedule.
/// </summary>
internal sealed partial class ChatCompletionHandler(
    DeploymentCatalog catalog,
    TokenizerRegistry tokenizers,
    ResponsePlanner planner,
    CompletionResponder responder,
    Random random,
    TimeProvider time,
    ILogger<ChatCompletionHandler> logger)
{
    public Task HandleOpenAiAsync(HttpContext context) => HandleAsync(context, routeDeployment: null);

    public Task HandleAzureAsync(HttpContext context, string deployment) => HandleAsync(context, deployment);

    private async Task HandleAsync(HttpContext context, string? routeDeployment)
    {
        // Latency is measured from request arrival, the same reference point clients use for TTFT.
        var startedAt = time.GetTimestamp();
        var cancellationToken = context.RequestAborted;

        try
        {
            var request = await ReadRequestAsync(context, cancellationToken);
            if (request is null)
            {
                return;
            }

            var name = routeDeployment ?? request.Model;
            if (string.IsNullOrWhiteSpace(name))
            {
                await ProtocolResponses.InvalidRequest(context, "'model' is required.", "model");
                return;
            }

            var maxOutputTokens = request.MaxCompletionTokens ?? request.MaxTokens;
            if (maxOutputTokens is < 1)
            {
                await ProtocolResponses.InvalidRequest(context, "max_tokens must be at least 1.", "max_tokens");
                return;
            }

            var deployment = catalog.Resolve(name);
            var inputTokens = PromptTokenCounter.Count(
                tokenizers.Get(deployment.Profile.Encoding),
                request.Messages!.Select(ToPromptMessage));

            var quota = deployment.Quota.TryAcquire(inputTokens + (maxOutputTokens ?? deployment.Profile.EstimatedOutputTokens));
            ProtocolResponses.ApplyRateLimitHeaders(context.Response, quota.Snapshot);
            if (!quota.Allowed)
            {
                LogThrottled(name, quota.RetryAfter);
                await ProtocolResponses.RateLimited(context, name, quota.RetryAfter, simulated: false);
                return;
            }

            if (await TryInjectRequestFaultAsync(context, deployment, cancellationToken))
            {
                return;
            }

            var plan = planner.Create(deployment.Profile, deployment.Faults, inputTokens, maxOutputTokens);
            var completion = new CompletionContext(
                Id: $"chatcmpl-{Guid.NewGuid():N}",
                Created: time.GetUtcNow().ToUnixTimeSeconds(),
                Model: deployment.Profile.ModelName ?? name,
                Encoding: deployment.Profile.Encoding,
                StartedAt: startedAt);

            if (request.Stream)
            {
                await responder.StreamAsync(context, plan, completion, request.StreamOptions?.IncludeUsage ?? false, cancellationToken);
            }
            else
            {
                await responder.CompleteAsync(context, plan, completion, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The client went away (its own timeout, or the load test ended). Expected under load; nothing to answer.
            LogClientDisconnected(context.Request.Path);
        }
    }

    private static async Task<ChatCompletionRequest?> ReadRequestAsync(HttpContext context, CancellationToken cancellationToken)
    {
        if (!context.Request.HasJsonContentType())
        {
            await ProtocolResponses.InvalidRequest(context, "Content-Type must be application/json.");
            return null;
        }

        ChatCompletionRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync(ProtocolJsonContext.Default.ChatCompletionRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            await ProtocolResponses.InvalidRequest(context, $"Request body is not valid JSON: {ex.Message}");
            return null;
        }

        if (request?.Messages is not { Count: > 0 })
        {
            await ProtocolResponses.InvalidRequest(context, "'messages' must be a non-empty array.", "messages");
            return null;
        }

        return request;
    }

    private async Task<bool> TryInjectRequestFaultAsync(HttpContext context, ResolvedDeployment deployment, CancellationToken cancellationToken)
    {
        var fault = FaultInjector.DecideRequestFault(deployment.Faults, random);
        if (fault == RequestFault.None)
        {
            return false;
        }

        LogFaultInjected(fault, deployment.Name);
        switch (fault)
        {
            case RequestFault.ServerError:
                await ProtocolResponses.ServerError(context);
                break;
            case RequestFault.ServiceUnavailable:
                await ProtocolResponses.ServiceUnavailable(context);
                break;
            case RequestFault.RateLimited:
                await ProtocolResponses.RateLimited(context, deployment.Name, TimeSpan.FromSeconds(1), simulated: true);
                break;
            case RequestFault.ContentFilter:
                await ProtocolResponses.ContentFilter(context);
                break;
            case RequestFault.Timeout:
                await Task.Delay(TimeSpan.FromMilliseconds(deployment.Faults.TimeoutMilliseconds), time, cancellationToken);
                await ProtocolResponses.GatewayTimeout(context);
                break;
            default:
                throw new InvalidOperationException($"Unhandled fault {fault}.");
        }

        return true;
    }

    private static PromptMessage ToPromptMessage(RequestMessage message) =>
        new(message.Role ?? "", ExtractText(message.Content), message.Name);

    /// <summary>Text of a string content or of the text parts of a content-part array. Images are not counted.</summary>
    private static string ExtractText(JsonElement content)
    {
        switch (content.ValueKind)
        {
            case JsonValueKind.String:
                return content.GetString() ?? "";
            case JsonValueKind.Array:
                var builder = new StringBuilder();
                foreach (var part in content.EnumerateArray())
                {
                    if (part.ValueKind == JsonValueKind.Object
                        && part.TryGetProperty("text", out var text)
                        && text.ValueKind == JsonValueKind.String)
                    {
                        builder.Append(text.GetString());
                    }
                }

                return builder.ToString();
            default:
                return "";
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Quota exhausted for {Deployment}; retry after {RetryAfter}")]
    private partial void LogThrottled(string deployment, TimeSpan retryAfter);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Injected {Fault} for {Deployment}")]
    private partial void LogFaultInjected(RequestFault fault, string deployment);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Client disconnected from {Path}")]
    private partial void LogClientDisconnected(PathString path);
}
