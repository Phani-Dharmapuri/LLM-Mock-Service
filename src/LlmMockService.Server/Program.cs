using LlmMockService.Core.Configuration;
using LlmMockService.Core.Deployments;
using LlmMockService.Core.Latency;
using LlmMockService.Core.Tokens;
using LlmMockService.Server.Admin;
using LlmMockService.Server.ChatCompletions;
using LlmMockService.Server.Configuration;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<LlmMockOptions>()
    .Bind(builder.Configuration.GetSection(LlmMockOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<LlmMockOptions>, LlmMockOptionsValidation>();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(Random.Shared);
builder.Services.AddSingleton(sp => new DeploymentCatalog(
    sp.GetRequiredService<IOptions<LlmMockOptions>>().Value,
    sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<TokenizerRegistry>();
builder.Services.AddSingleton<OutputTextLibrary>();
builder.Services.AddSingleton<ResponsePlanner>();
builder.Services.AddSingleton<CompletionResponder>();
builder.Services.AddSingleton<ChatCompletionHandler>();

var app = builder.Build();

var options = app.Services.GetRequiredService<IOptions<LlmMockOptions>>().Value;
WarmUpTokenizers(app.Services, options);

app.MapGet("/healthz", () => TypedResults.Ok("ok"));

// OpenAI clients put /v1 in the base URL; some configure it without, so accept both.
app.MapPost("/v1/chat/completions", (HttpContext context, ChatCompletionHandler handler) => handler.HandleOpenAiAsync(context));
app.MapPost("/chat/completions", (HttpContext context, ChatCompletionHandler handler) => handler.HandleOpenAiAsync(context));
app.MapPost(
    "/openai/deployments/{deployment}/chat/completions",
    (HttpContext context, string deployment, ChatCompletionHandler handler) => handler.HandleAzureAsync(context, deployment));

if (options.AdminApiEnabled)
{
    app.MapAdminEndpoints();
}

app.Run();

// Load each encoding's vocabulary at startup, so the first request is not slow and missing data fails here.
static void WarmUpTokenizers(IServiceProvider services, LlmMockOptions options)
{
    var library = services.GetRequiredService<OutputTextLibrary>();
    foreach (var encoding in options.Profiles.Values.Select(p => p.Encoding).Distinct(StringComparer.OrdinalIgnoreCase))
    {
        _ = library.GetPieces(encoding);
    }
}

/// <summary>Entry point; public so integration tests can host it with WebApplicationFactory.</summary>
public partial class Program;
