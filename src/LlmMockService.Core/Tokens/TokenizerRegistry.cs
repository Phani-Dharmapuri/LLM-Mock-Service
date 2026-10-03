using System.Collections.Concurrent;
using Microsoft.ML.Tokenizers;

namespace LlmMockService.Core.Tokens;

/// <summary>Creates each tiktoken encoding once; construction loads a large vocabulary.</summary>
public sealed class TokenizerRegistry
{
    public static IReadOnlyList<string> SupportedEncodings { get; } = ["o200k_base", "cl100k_base"];

    private readonly ConcurrentDictionary<string, Lazy<Tokenizer>> _tokenizers = new(StringComparer.OrdinalIgnoreCase);

    public static bool IsSupported(string encoding) =>
        SupportedEncodings.Contains(encoding, StringComparer.OrdinalIgnoreCase);

    public Tokenizer Get(string encoding)
    {
        if (!IsSupported(encoding))
        {
            throw new ArgumentException($"Unsupported encoding '{encoding}'.", nameof(encoding));
        }

        return _tokenizers.GetOrAdd(
            encoding,
            static name => new Lazy<Tokenizer>(() => TiktokenTokenizer.CreateForEncoding(name.ToLowerInvariant()))).Value;
    }
}
