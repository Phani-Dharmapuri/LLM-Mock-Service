using LlmMockService.Core.Tokens;

namespace LlmMockService.Core.Configuration;

/// <summary>Validates configuration up front so a bad profile fails at startup, not mid-test.</summary>
public static class LlmMockOptionsValidator
{
    public static IReadOnlyList<string> Validate(LlmMockOptions options)
    {
        var errors = new List<string>();

        if (options.StreamTickMilliseconds < 1)
        {
            errors.Add($"{nameof(LlmMockOptions.StreamTickMilliseconds)} must be at least 1.");
        }

        if (!options.Profiles.ContainsKey(options.DefaultProfile))
        {
            errors.Add($"{nameof(LlmMockOptions.DefaultProfile)} '{options.DefaultProfile}' is not defined in Profiles.");
        }

        foreach (var (name, profile) in options.Profiles)
        {
            ValidateProfile($"Profiles:{name}", profile, errors);
        }

        foreach (var (name, deployment) in options.Deployments)
        {
            var path = $"Deployments:{name}";
            if (!options.Profiles.ContainsKey(deployment.Profile))
            {
                errors.Add($"{path}: profile '{deployment.Profile}' is not defined in Profiles.");
            }

            if (deployment.RequestsPerMinute < 0)
            {
                errors.Add($"{path}: RequestsPerMinute cannot be negative.");
            }

            if (deployment.TokensPerMinute < 0)
            {
                errors.Add($"{path}: TokensPerMinute cannot be negative.");
            }

            errors.AddRange(ValidateFaults(deployment.Faults).Select(e => $"{path}:Faults: {e}"));
        }

        return errors;
    }

    public static IReadOnlyList<string> ValidateFaults(FaultOptions faults)
    {
        var errors = new List<string>();
        (string Name, double Value)[] rates =
        [
            (nameof(FaultOptions.ServerErrorRate), faults.ServerErrorRate),
            (nameof(FaultOptions.ServiceUnavailableRate), faults.ServiceUnavailableRate),
            (nameof(FaultOptions.RateLimitRate), faults.RateLimitRate),
            (nameof(FaultOptions.ContentFilterRate), faults.ContentFilterRate),
            (nameof(FaultOptions.TimeoutRate), faults.TimeoutRate),
            (nameof(FaultOptions.MidStreamDisconnectRate), faults.MidStreamDisconnectRate),
            (nameof(FaultOptions.StallRate), faults.StallRate),
        ];

        foreach (var (name, value) in rates)
        {
            if (value is < 0 or > 1 || double.IsNaN(value))
            {
                errors.Add($"{name} must be between 0 and 1 (was {value}).");
            }
        }

        if (faults.RequestFaultRateTotal > 1 + 1e-9)
        {
            errors.Add($"request-level fault rates must sum to at most 1 (was {faults.RequestFaultRateTotal}).");
        }

        if (faults.TimeoutMilliseconds < 0)
        {
            errors.Add($"{nameof(FaultOptions.TimeoutMilliseconds)} cannot be negative.");
        }

        if (faults.StallMilliseconds < 0)
        {
            errors.Add($"{nameof(FaultOptions.StallMilliseconds)} cannot be negative.");
        }

        return errors;
    }

    private static void ValidateProfile(string path, LatencyProfileOptions profile, List<string> errors)
    {
        if (!TokenizerRegistry.IsSupported(profile.Encoding))
        {
            errors.Add($"{path}: encoding '{profile.Encoding}' is not supported. Use one of: {string.Join(", ", TokenizerRegistry.SupportedEncodings)}.");
        }

        ValidateDistribution($"{path}:{nameof(profile.TimeToFirstTokenMs)}", profile.TimeToFirstTokenMs, errors);
        ValidateDistribution($"{path}:{nameof(profile.InterTokenLatencyMs)}", profile.InterTokenLatencyMs, errors);
        ValidateDistribution($"{path}:{nameof(profile.OutputTokens)}", profile.OutputTokens, errors);

        if (profile.PrefillMsPer1kInputTokens < 0)
        {
            errors.Add($"{path}: PrefillMsPer1kInputTokens cannot be negative.");
        }

        if (profile.InterTokenJitter is < 0 or > 1)
        {
            errors.Add($"{path}: InterTokenJitter must be between 0 and 1.");
        }

        if (profile.MaxOutputTokens < 1)
        {
            errors.Add($"{path}: MaxOutputTokens must be at least 1.");
        }
    }

    private static void ValidateDistribution(string path, DistributionOptions distribution, List<string> errors)
    {
        if (distribution.Median < 0 || distribution.P99 < 0)
        {
            errors.Add($"{path}: Median and P99 cannot be negative.");
        }
        else if (distribution.P99 < distribution.Median)
        {
            errors.Add($"{path}: P99 ({distribution.P99}) must be greater than or equal to Median ({distribution.Median}).");
        }
        else if (distribution.Median == 0 && distribution.P99 > 0)
        {
            errors.Add($"{path}: Median must be greater than 0 when P99 is greater than 0.");
        }
    }
}
