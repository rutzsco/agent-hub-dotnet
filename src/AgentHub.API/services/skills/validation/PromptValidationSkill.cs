namespace AgentHub.API.Services.Skills.Validation;

/// <summary>
/// Input for prompt validation.
/// </summary>
public sealed class PromptValidationInput
{
    public required string Prompt { get; init; }
    public required string UserId { get; init; }
}

/// <summary>
/// Validates prompts for safety before processing by agents.
/// Checks for prompt injection, jailbreak attempts, and other malicious patterns.
/// </summary>
public sealed class PromptValidationSkill : ISkill<PromptValidationInput, PromptValidationResult>
{
    private readonly IReadOnlyList<ValidationRule> _rules;
    private readonly ILogger<PromptValidationSkill> _logger;

    /// <summary>Constructs the skill with the default rule set from <see cref="ValidationRules.DefaultRules"/>.</summary>
    public PromptValidationSkill(ILogger<PromptValidationSkill> logger)
        : this(ValidationRules.DefaultRules, logger)
    {
    }

    /// <summary>Constructs the skill with a custom rule set (primarily for tests or feature flags).</summary>
    public PromptValidationSkill(
        IReadOnlyList<ValidationRule> rules,
        ILogger<PromptValidationSkill> logger)
    {
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<PromptValidationResult> ExecuteAsync(
        PromptValidationInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (string.IsNullOrWhiteSpace(input.Prompt))
        {
            _logger.LogWarning(
                "Prompt validation failed. Rule=MinimumLength, UserId={UserId}, Error=Empty prompt",
                input.UserId ?? "unknown");

            return Task.FromResult(PromptValidationResult.Failure(
                "Message cannot be empty.",
                "MinimumLength"));
        }

        if (string.IsNullOrWhiteSpace(input.UserId))
        {
            _logger.LogWarning("Prompt validation failed. Missing or invalid userId.");
            return Task.FromResult(PromptValidationResult.Failure(
                "UserId is required.",
                "InvalidUserId"));
        }

        foreach (var rule in _rules)
        {
            var (isValid, errorMessage) = rule.Validator(input.Prompt);
            if (!isValid)
            {
                _logger.LogWarning(
                    "Prompt validation failed. Rule={RuleName}, UserId={UserId}, Error={ErrorMessage}",
                    rule.Name,
                    input.UserId,
                    errorMessage);

                return Task.FromResult(PromptValidationResult.Failure(
                    errorMessage ?? "Validation failed",
                    rule.Name));
            }
        }

        _logger.LogDebug(
            "Prompt validation succeeded. UserId={UserId}, PromptLength={PromptLength}",
            input.UserId,
            input.Prompt.Length);

        return Task.FromResult(PromptValidationResult.Success());
    }
}
