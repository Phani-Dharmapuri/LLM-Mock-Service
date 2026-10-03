using Microsoft.ML.Tokenizers;

namespace LlmMockService.Core.Tokens;

public readonly record struct PromptMessage(string Role, string Content, string? Name = null);

/// <summary>
/// Counts chat prompt tokens with the per-message framing overhead used by current OpenAI chat models
/// (3 tokens per message, +1 for a name, +3 to prime the reply). Tool definitions and images are not counted.
/// </summary>
public static class PromptTokenCounter
{
    internal const int TokensPerMessage = 3;
    internal const int TokensPerName = 1;
    internal const int ReplyPrimingTokens = 3;

    public static int Count(Tokenizer tokenizer, IEnumerable<PromptMessage> messages)
    {
        var total = ReplyPrimingTokens;
        foreach (var message in messages)
        {
            total += TokensPerMessage + tokenizer.CountTokens(message.Role) + tokenizer.CountTokens(message.Content);
            if (!string.IsNullOrEmpty(message.Name))
            {
                total += TokensPerName + tokenizer.CountTokens(message.Name);
            }
        }

        return total;
    }
}
