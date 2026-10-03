using System.Collections.Concurrent;
using System.Text;

namespace LlmMockService.Core.Tokens;

/// <summary>
/// Supplies completion text one real token at a time. A fixed corpus is tokenized once per encoding, so
/// streaming N chunks costs no tokenizer work and each chunk is exactly one token of that encoding.
/// </summary>
public sealed class OutputTextLibrary(TokenizerRegistry tokenizers)
{
    // Original filler prose. ASCII only, so every token decodes to a complete string on its own.
    internal const string Corpus =
        "Performance testing an AI application is mostly about the parts you own. The model is a dependency " +
        "with its own latency profile, and the interesting questions are how your orchestration layer behaves " +
        "around it: how quickly the first token reaches the user, how retries and fallbacks react to throttling, " +
        "how many concurrent streams a single instance can hold, and whether token accounting stays accurate " +
        "under load. A simulated model lets you answer those questions repeatedly, at any scale, without paying " +
        "for inference. The numbers are only as good as the profile behind them, so calibrate it from production " +
        "telemetry and validate it against a small run on the real service before trusting the results. ";

    private readonly ConcurrentDictionary<string, string[]> _pieces = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> GetPieces(string encoding) => _pieces.GetOrAdd(encoding, Tokenize);

    /// <summary>The text of output token <paramref name="index"/>, cycling through the corpus.</summary>
    public string TokenAt(string encoding, int index)
    {
        var pieces = GetPieces(encoding);
        return pieces[index % pieces.Count];
    }

    public string Compose(string encoding, int tokenCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tokenCount);
        var pieces = GetPieces(encoding);
        var builder = new StringBuilder(tokenCount * 5);
        for (var i = 0; i < tokenCount; i++)
        {
            builder.Append(pieces[i % pieces.Count]);
        }

        return builder.ToString();
    }

    private string[] Tokenize(string encoding)
    {
        var tokenizer = tokenizers.Get(encoding);
        return tokenizer.EncodeToIds(Corpus)
            .Select(id => tokenizer.Decode([id]) ?? throw new InvalidOperationException($"Token {id} did not decode for '{encoding}'."))
            .ToArray();
    }
}
