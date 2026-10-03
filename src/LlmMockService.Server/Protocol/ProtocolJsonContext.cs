using System.Text.Json.Serialization;

namespace LlmMockService.Server.Protocol;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ChatCompletionRequest))]
[JsonSerializable(typeof(ChatCompletion))]
[JsonSerializable(typeof(ChatCompletionChunk))]
[JsonSerializable(typeof(ErrorResponse))]
internal sealed partial class ProtocolJsonContext : JsonSerializerContext;
