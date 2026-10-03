using LlmMockService.Core.Configuration;
using Microsoft.Extensions.Options;

namespace LlmMockService.Server.Configuration;

internal sealed class LlmMockOptionsValidation : IValidateOptions<LlmMockOptions>
{
    public ValidateOptionsResult Validate(string? name, LlmMockOptions options)
    {
        var errors = LlmMockOptionsValidator.Validate(options);
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
