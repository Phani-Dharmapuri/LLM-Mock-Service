using System.Text.Json;

namespace LlmMockService.Server.Protocol;

// Subset of the OpenAI chat completions wire format, shared by OpenAI and Azure OpenAI.
// Unknown request fields (tools, temperature, ...) are accepted and ignored.

internal sealed class ChatCompletionRequest
{
    public string? Model { get; set; }
    public List<RequestMessage>? Messages { get; set; }
    public bool Stream { get; set; }
    public RequestStreamOptions? StreamOptions { get; set; }
    public int? MaxTokens { get; set; }
    public int? MaxCompletionTokens { get; set; }
}

internal sealed class RequestMessage
{
    public string? Role { get; set; }

    /// <summary>Either a string or an array of content parts.</summary>
    public JsonElement Content { get; set; }

    public string? Name { get; set; }
}

internal sealed class RequestStreamOptions
{
    public bool IncludeUsage { get; set; }
}

internal sealed record ChatCompletion(
    string Id,
    string Object,
    long Created,
    string Model,
    string SystemFingerprint,
    IReadOnlyList<ChatChoice> Choices,
    Usage Usage);

internal sealed record ChatChoice(int Index, AssistantMessage Message, string FinishReason);

internal sealed record AssistantMessage(string Role, string Content);

internal sealed record Usage(int PromptTokens, int CompletionTokens, int TotalTokens);

internal sealed record ChatCompletionChunk(
    string Id,
    string Object,
    long Created,
    string Model,
    string SystemFingerprint,
    IReadOnlyList<ChunkChoice> Choices,
    Usage? Usage);

internal sealed record ChunkChoice(int Index, ChunkDelta Delta, string? FinishReason);

internal sealed record ChunkDelta(string? Role, string? Content);

internal sealed record ErrorResponse(ErrorBody Error);

internal sealed record ErrorBody(string Message, string Type, string? Param, string? Code);
