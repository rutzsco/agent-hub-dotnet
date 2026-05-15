namespace AgentHub.API.Services.Skills.Validation;

/// <summary>
/// Result of prompt validation.
/// </summary>
public sealed class PromptValidationResult
{
    /// <summary>
    /// Gets a value indicating whether the prompt passed validation.
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// Gets the validation error message if validation failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Gets the specific validation rule that failed, if any.
    /// </summary>
    public string? FailedRule { get; init; }

    /// <summary>
    /// Gets the sanitized prompt if modifications were made.
    /// </summary>
    public string? SanitizedPrompt { get; init; }

    public static PromptValidationResult Success(string? sanitizedPrompt = null) => new()
    {
        IsValid = true,
        SanitizedPrompt = sanitizedPrompt
    };

    public static PromptValidationResult Failure(string errorMessage, string failedRule) => new()
    {
        IsValid = false,
        ErrorMessage = errorMessage,
        FailedRule = failedRule
    };
}
