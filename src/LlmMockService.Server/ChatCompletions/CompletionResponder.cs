using System.Buffers;
using System.IO.Pipelines;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using LlmMockService.Core.Configuration;
using LlmMockService.Core.Latency;
using LlmMockService.Core.Tokens;
using LlmMockService.Server.Protocol;
using Microsoft.Extensions.Options;

namespace LlmMockService.Server.ChatCompletions;

internal sealed record CompletionContext(string Id, long Created, string Model, string Encoding, long StartedAt);

/// <summary>Writes a <see cref="ResponsePlan"/> to the wire on schedule, as SSE or as a single JSON body.</summary>
internal sealed class CompletionResponder(OutputTextLibrary text, TimeProvider time, IOptions<LlmMockOptions> options)
{
    private const string SystemFingerprint = "fp_llmmock";

    private readonly TimeSpan _tick = TimeSpan.FromMilliseconds(options.Value.StreamTickMilliseconds);

    public async Task CompleteAsync(HttpContext context, ResponsePlan plan, CompletionContext completion, CancellationToken cancellationToken)
    {
        var disconnectAt = plan.DisconnectAfterTokens;
        await DelayUntilAsync(completion.StartedAt, disconnectAt is { } d ? plan.TokenDueAt(d) : plan.TotalDuration, cancellationToken);
        if (disconnectAt is not null)
        {
            context.Abort();
            return;
        }

        var body = new ChatCompletion(
            completion.Id,
            "chat.completion",
            completion.Created,
            completion.Model,
            SystemFingerprint,
            [new ChatChoice(0, new AssistantMessage("assistant", text.Compose(completion.Encoding, plan.OutputTokens)), FinishReasonText(plan))],
            UsageOf(plan));

        await context.Response.WriteAsJsonAsync(body, ProtocolJsonContext.Default.ChatCompletion, contentType: null, cancellationToken);
    }

    /// <summary>
    /// Emits one SSE event per token. Rather than sleeping per token, it sleeps until the next token is due
    /// (at least one tick after the first token), then writes every token that has fallen due and flushes once.
    /// Timer jitter therefore delays a batch slightly but never accumulates across the stream.
    /// </summary>
    public async Task StreamAsync(HttpContext context, ResponsePlan plan, CompletionContext completion, bool includeUsage, CancellationToken cancellationToken)
    {
        var response = context.Response;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        await response.StartAsync(cancellationToken);

        var writer = response.BodyWriter;
        var sent = 0;
        while (sent < plan.OutputTokens)
        {
            if (plan.DisconnectAfterTokens == sent)
            {
                context.Abort();
                return;
            }

            var elapsed = await WaitForTokenAsync(plan, sent, completion.StartedAt, cancellationToken);
            var due = Math.Max(plan.TokensDueBy(elapsed), sent + 1);
            if (plan.DisconnectAfterTokens is { } disconnectAt && disconnectAt > sent)
            {
                due = Math.Min(due, disconnectAt);
            }

            for (var i = sent; i < due; i++)
            {
                var delta = new ChunkDelta(i == 0 ? "assistant" : null, text.TokenAt(completion.Encoding, i));
                WriteEvent(writer, Chunk(completion, new ChunkChoice(0, delta, null)), ProtocolJsonContext.Default.ChatCompletionChunk);
            }

            await writer.FlushAsync(cancellationToken);
            sent = due;
        }

        WriteEvent(writer, Chunk(completion, new ChunkChoice(0, new ChunkDelta(null, null), FinishReasonText(plan))), ProtocolJsonContext.Default.ChatCompletionChunk);
        if (includeUsage)
        {
            WriteEvent(writer, Chunk(completion, choice: null) with { Usage = UsageOf(plan) }, ProtocolJsonContext.Default.ChatCompletionChunk);
        }

        writer.Write("data: [DONE]\n\n"u8);
        await writer.FlushAsync(cancellationToken);
    }

    /// <summary>Waits until token <paramref name="index"/> is due and returns the elapsed time on waking.</summary>
    private async Task<TimeSpan> WaitForTokenAsync(ResponsePlan plan, int index, long startedAt, CancellationToken cancellationToken)
    {
        var elapsed = time.GetElapsedTime(startedAt);
        var wait = plan.TokenDueAt(index) - elapsed;
        if (wait <= TimeSpan.Zero)
        {
            return elapsed;
        }

        // The first token waits exactly, so TTFT stays precise; later tokens are batched per tick.
        if (index > 0 && wait < _tick)
        {
            wait = _tick;
        }

        await Task.Delay(wait, time, cancellationToken);
        return time.GetElapsedTime(startedAt);
    }

    private async Task DelayUntilAsync(long startedAt, TimeSpan dueAt, CancellationToken cancellationToken)
    {
        var wait = dueAt - time.GetElapsedTime(startedAt);
        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, time, cancellationToken);
        }
    }

    private static ChatCompletionChunk Chunk(CompletionContext completion, ChunkChoice? choice) => new(
        completion.Id,
        "chat.completion.chunk",
        completion.Created,
        completion.Model,
        SystemFingerprint,
        choice is null ? [] : [choice],
        Usage: null);

    private static void WriteEvent<T>(PipeWriter writer, T value, JsonTypeInfo<T> typeInfo)
    {
        writer.Write("data: "u8);
        using (var json = new Utf8JsonWriter(writer))
        {
            JsonSerializer.Serialize(json, value, typeInfo);
        }

        writer.Write("\n\n"u8);
    }

    private static Usage UsageOf(ResponsePlan plan) =>
        new(plan.InputTokens, plan.OutputTokens, plan.InputTokens + plan.OutputTokens);

    private static string FinishReasonText(ResponsePlan plan) => plan.FinishReason switch
    {
        FinishReason.Stop => "stop",
        FinishReason.Length => "length",
        _ => throw new InvalidOperationException($"Unhandled finish reason {plan.FinishReason}."),
    };
}
