using System.Globalization;
using LlmMockService.Core.Quotas;

namespace LlmMockService.Server.Protocol;

/// <summary>Error bodies and headers in the shape OpenAI and Azure OpenAI clients parse.</summary>
internal static class ProtocolResponses
{
    private const string Simulated = " (Simulated by LlmMockService.)";

    public static Task InvalidRequest(HttpContext context, string message, string? param = null) =>
        Write(context, StatusCodes.Status400BadRequest, new ErrorBody(message, "invalid_request_error", param, null));

    public static Task ContentFilter(HttpContext context) =>
        Write(context, StatusCodes.Status400BadRequest, new ErrorBody(
            "The response was filtered due to the prompt triggering the content management policy." + Simulated,
            "invalid_request_error",
            "prompt",
            "content_filter"));

    public static Task ServerError(HttpContext context) =>
        Write(context, StatusCodes.Status500InternalServerError, new ErrorBody(
            "The server had an error while processing your request." + Simulated, "server_error", null, null));

    public static Task ServiceUnavailable(HttpContext context) =>
        Write(context, StatusCodes.Status503ServiceUnavailable, new ErrorBody(
            "The service is temporarily unable to process your request." + Simulated, "service_unavailable", null, null));

    public static Task GatewayTimeout(HttpContext context) =>
        Write(context, StatusCodes.Status504GatewayTimeout, new ErrorBody(
            "The request timed out." + Simulated, "timeout", null, null));

    public static Task RateLimited(HttpContext context, string deployment, TimeSpan retryAfter, bool simulated)
    {
        var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        context.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        context.Response.Headers["retry-after-ms"] = Math.Max(1, (long)Math.Ceiling(retryAfter.TotalMilliseconds)).ToString(CultureInfo.InvariantCulture);

        var message = $"Rate limit exceeded for deployment '{deployment}'. Please retry after {seconds} second{(seconds == 1 ? "" : "s")}."
            + (simulated ? Simulated : "");
        return Write(context, StatusCodes.Status429TooManyRequests, new ErrorBody(message, "requests", null, "rate_limit_exceeded"));
    }

    /// <summary>x-ratelimit-* headers, sent on every response for deployments that have a quota.</summary>
    public static void ApplyRateLimitHeaders(HttpResponse response, QuotaSnapshot quota)
    {
        if (quota.RequestLimit is { } requestLimit)
        {
            response.Headers["x-ratelimit-limit-requests"] = requestLimit.ToString(CultureInfo.InvariantCulture);
            response.Headers["x-ratelimit-remaining-requests"] = Math.Max(0, quota.RemainingRequests).ToString(CultureInfo.InvariantCulture);
        }

        if (quota.TokenLimit is { } tokenLimit)
        {
            response.Headers["x-ratelimit-limit-tokens"] = tokenLimit.ToString(CultureInfo.InvariantCulture);
            response.Headers["x-ratelimit-remaining-tokens"] = Math.Max(0, quota.RemainingTokens).ToString(CultureInfo.InvariantCulture);
        }
    }

    private static Task Write(HttpContext context, int statusCode, ErrorBody error)
    {
        context.Response.StatusCode = statusCode;
        return context.Response.WriteAsJsonAsync(new ErrorResponse(error), ProtocolJsonContext.Default.ErrorResponse, contentType: null, context.RequestAborted);
    }
}
