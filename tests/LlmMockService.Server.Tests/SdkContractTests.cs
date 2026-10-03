using System.Diagnostics;
using System.Text;
using LlmMockService.Core.Tokens;
using OpenAI.Chat;

namespace LlmMockService.Server.Tests;

/// <summary>Proves the official client SDKs accept the mock's responses, streaming and non-streaming.</summary>
public class SdkContractTests(MockServerFactory factory) : IClassFixture<MockServerFactory>
{
    private static readonly ChatMessage[] Messages =
    [
        new SystemChatMessage("You are a concise assistant."),
        new UserChatMessage("Summarise the benefits of load testing."),
    ];

    private static readonly TokenizerRegistry Tokenizers = new();

    private static int ExpectedInputTokens => PromptTokenCounter.Count(
        Tokenizers.Get("o200k_base"),
        [new("system", "You are a concise assistant."), new("user", "Summarise the benefits of load testing.")]);

    private static string ExpectedText(int tokens) => new OutputTextLibrary(Tokenizers).Compose("o200k_base", tokens);

    public static TheoryData<string> Flavours => ["openai", "azure"];

    [Theory]
    [MemberData(nameof(Flavours))]
    public async Task Non_streaming_completion_has_text_usage_and_finish_reason(string flavour)
    {
        var client = ClientFor(flavour, MockServerFactory.Instant);

        ChatCompletion completion = await client.CompleteChatAsync(Messages, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ExpectedText(MockServerFactory.InstantOutputTokens), completion.Content.Single().Text);
        Assert.Equal(ChatFinishReason.Stop, completion.FinishReason);
        Assert.Equal(ChatMessageRole.Assistant, completion.Role);
        Assert.Equal(ExpectedInputTokens, completion.Usage.InputTokenCount);
        Assert.Equal(MockServerFactory.InstantOutputTokens, completion.Usage.OutputTokenCount);
        Assert.StartsWith("chatcmpl-", completion.Id, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Flavours))]
    public async Task Streaming_completion_sends_one_update_per_token_then_usage(string flavour)
    {
        var client = ClientFor(flavour, MockServerFactory.Instant);
        var text = new StringBuilder();
        var contentUpdates = 0;
        ChatTokenUsage? usage = null;
        ChatFinishReason? finishReason = null;

        await foreach (var update in client.CompleteChatStreamingAsync(Messages, cancellationToken: TestContext.Current.CancellationToken))
        {
            foreach (var part in update.ContentUpdate)
            {
                text.Append(part.Text);
                contentUpdates++;
            }

            usage ??= update.Usage;
            finishReason ??= update.FinishReason;
        }

        Assert.Equal(ExpectedText(MockServerFactory.InstantOutputTokens), text.ToString());
        Assert.Equal(MockServerFactory.InstantOutputTokens, contentUpdates);
        Assert.Equal(ChatFinishReason.Stop, finishReason);
        Assert.NotNull(usage);
        Assert.Equal(ExpectedInputTokens, usage.InputTokenCount);
        Assert.Equal(MockServerFactory.InstantOutputTokens, usage.OutputTokenCount);
    }

    [Fact]
    public async Task Max_output_tokens_truncates_with_length_finish_reason()
    {
        var client = factory.OpenAiChatClient(MockServerFactory.Instant);

        ChatCompletion completion = await client.CompleteChatAsync(
            Messages,
            new ChatCompletionOptions { MaxOutputTokenCount = 5 },
            TestContext.Current.CancellationToken);

        Assert.Equal(ChatFinishReason.Length, completion.FinishReason);
        Assert.Equal(5, completion.Usage.OutputTokenCount);
        Assert.Equal(ExpectedText(5), completion.Content.Single().Text);
    }

    [Fact]
    public async Task Streaming_honours_ttft_and_inter_token_schedule()
    {
        var client = factory.AzureChatClient(MockServerFactory.Slow);
        var stopwatch = Stopwatch.StartNew();
        TimeSpan? firstToken = null;
        var tokens = 0;

        await foreach (var update in client.CompleteChatStreamingAsync(Messages, cancellationToken: TestContext.Current.CancellationToken))
        {
            if (update.ContentUpdate.Count > 0)
            {
                firstToken ??= stopwatch.Elapsed;
                tokens += update.ContentUpdate.Count;
            }
        }

        var total = stopwatch.Elapsed;
        const int expectedTotalMs = MockServerFactory.SlowTtftMs + ((MockServerFactory.SlowOutputTokens - 1) * MockServerFactory.SlowItlMs);

        // Never early. The upper bound is loose because CI machines are noisy; the accuracy benchmark measures it properly.
        Assert.Equal(MockServerFactory.SlowOutputTokens, tokens);
        Assert.InRange(firstToken!.Value.TotalMilliseconds, MockServerFactory.SlowTtftMs, MockServerFactory.SlowTtftMs + 250);
        Assert.InRange(total.TotalMilliseconds, expectedTotalMs, expectedTotalMs + 500);
    }

    private ChatClient ClientFor(string flavour, string deployment) => flavour switch
    {
        "openai" => factory.OpenAiChatClient(deployment),
        "azure" => factory.AzureChatClient(deployment),
        _ => throw new ArgumentOutOfRangeException(nameof(flavour), flavour, null),
    };
}
