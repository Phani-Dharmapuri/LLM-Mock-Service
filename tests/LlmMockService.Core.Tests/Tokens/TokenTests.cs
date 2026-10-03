using LlmMockService.Core.Tokens;

namespace LlmMockService.Core.Tests.Tokens;

public class TokenTests
{
    private static readonly TokenizerRegistry Registry = new();

    [Theory]
    [InlineData("o200k_base")]
    [InlineData("cl100k_base")]
    public void Prompt_count_adds_chat_framing_overhead(string encoding)
    {
        var tokenizer = Registry.Get(encoding);
        PromptMessage[] messages =
        [
            new("system", "You are a concise assistant."),
            new("user", "Summarise the quarterly report in three bullet points.", Name: "alice"),
        ];

        var expected = PromptTokenCounter.ReplyPrimingTokens
            + messages.Sum(m => PromptTokenCounter.TokensPerMessage + tokenizer.CountTokens(m.Role) + tokenizer.CountTokens(m.Content))
            + PromptTokenCounter.TokensPerName + tokenizer.CountTokens("alice");

        Assert.Equal(expected, PromptTokenCounter.Count(tokenizer, messages));
    }

    [Fact]
    public void Registry_rejects_unsupported_encoding() =>
        Assert.Throws<ArgumentException>(() => Registry.Get("r50k_base"));

    [Fact]
    public void Registry_returns_the_same_instance_per_encoding() =>
        Assert.Same(Registry.Get("o200k_base"), Registry.Get("O200K_BASE"));

    [Theory]
    [InlineData("o200k_base")]
    [InlineData("cl100k_base")]
    public void Output_pieces_are_real_tokens_of_the_encoding(string encoding)
    {
        var library = new OutputTextLibrary(Registry);
        var pieces = library.GetPieces(encoding);

        // The corpus re-tokenizes to exactly the pieces it was split into.
        Assert.Equal(pieces.Count, Registry.Get(encoding).CountTokens(library.Compose(encoding, pieces.Count)));
        Assert.All(pieces, p => Assert.False(string.IsNullOrEmpty(p)));
    }

    [Fact]
    public void Compose_cycles_through_the_corpus_token_by_token()
    {
        var library = new OutputTextLibrary(Registry);
        var count = library.GetPieces("o200k_base").Count + 5;

        var expected = string.Concat(Enumerable.Range(0, count).Select(i => library.TokenAt("o200k_base", i)));

        Assert.Equal(expected, library.Compose("o200k_base", count));
    }
}
