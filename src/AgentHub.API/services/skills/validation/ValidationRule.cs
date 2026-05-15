using System.Text.RegularExpressions;

namespace AgentHub.API.Services.Skills.Validation;

/// <summary>
/// Represents a validation rule for prompt safety.
/// </summary>
public sealed class ValidationRule
{
    public required string Name { get; init; }
    public required Func<string, (bool IsValid, string? ErrorMessage)> Validator { get; init; }
}

/// <summary>
/// Pre-defined validation rules for prompt safety.
/// </summary>
public static class ValidationRules
{
    private static readonly Regex PromptInjectionPattern = new(
        @"(ignore\s+((previous|above|all|prior|earlier|the)\s+)*(instructions?|prompts?|rules?|commands?)|" +
        @"you\s+are\s+(now|currently|a)\s+(different|new)|" +
        @"you\s+are\s+now\s+a|" +
        @"system\s*:\s*ignore|" +
        @"<\|im_start\|>|<\|im_end\|>|" +
        @"###\s*Instruction|" +
        @"CRITICAL\s+SECURITY\s+OVERRIDE)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex JailbreakPattern = new(
        @"(dan\s+mode|" +
        @"do\s+anything\s+now|" +
        @"developer\s+mode|" +
        @"sudo\s+mode|" +
        @"god\s+mode|" +
        @"jailbreak|" +
        @"bypass\s+(content|safety|filter)|" +
        @"unrestricted\s+mode)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RoleManipulationPattern = new(
        @"(you\s+must\s+pretend|" +
        @"act\s+as\s+if\s+you\s+(are|have)\s+no|" +
        @"forget\s+(you\s+are|your\s+role)|" +
        @"you\s+are\s+no\s+longer|" +
        @"from\s+now\s+on\s+you\s+will)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ExcessiveRepetitionPattern = new(
        @"(.{3,})\1{10,}",
        RegexOptions.Compiled);

    public static readonly ValidationRule PromptInjection = new()
    {
        Name = "PromptInjection",
        Validator = prompt =>
        {
            if (PromptInjectionPattern.IsMatch(prompt))
            {
                return (false, "Potential prompt injection detected. The message contains patterns that attempt to override system instructions.");
            }
            return (true, null);
        }
    };

    public static readonly ValidationRule JailbreakAttempt = new()
    {
        Name = "JailbreakAttempt",
        Validator = prompt =>
        {
            if (JailbreakPattern.IsMatch(prompt))
            {
                return (false, "Potential jailbreak attempt detected. The message contains patterns that attempt to bypass safety constraints.");
            }
            return (true, null);
        }
    };

    public static readonly ValidationRule RoleManipulation = new()
    {
        Name = "RoleManipulation",
        Validator = prompt =>
        {
            if (RoleManipulationPattern.IsMatch(prompt))
            {
                return (false, "Role manipulation detected. The message attempts to alter the agent's behavior or identity.");
            }
            return (true, null);
        }
    };

    public static readonly ValidationRule ExcessiveRepetition = new()
    {
        Name = "ExcessiveRepetition",
        Validator = prompt =>
        {
            if (ExcessiveRepetitionPattern.IsMatch(prompt))
            {
                return (false, "Excessive repetition detected. The message contains repeated patterns that may indicate malicious input.");
            }
            return (true, null);
        }
    };

    public static readonly ValidationRule ValidCharacters = new()
    {
        Name = "ValidCharacters",
        Validator = prompt =>
        {
            foreach (char c in prompt)
            {
                if (char.IsControl(c) && c != '\n' && c != '\r' && c != '\t')
                {
                    return (false, "Invalid control characters detected in the message.");
                }
            }
            return (true, null);
        }
    };

    public static readonly ValidationRule MinimumLength = new()
    {
        Name = "MinimumLength",
        Validator = prompt =>
        {
            var trimmed = prompt.Trim();
            if (trimmed.Length == 0)
            {
                return (false, "Message cannot be empty.");
            }
            if (trimmed.Length < 1)
            {
                return (false, "Message is too short.");
            }
            return (true, null);
        }
    };

    public static readonly ValidationRule MaximumLength = new()
    {
        Name = "MaximumLength",
        Validator = prompt =>
        {
            if (prompt.Length > 4000)
            {
                return (false, $"Message exceeds maximum length of 4000 characters (current: {prompt.Length}).");
            }
            return (true, null);
        }
    };

    public static IReadOnlyList<ValidationRule> DefaultRules => new[]
    {
        MinimumLength,
        MaximumLength,
        ValidCharacters,
        PromptInjection,
        JailbreakAttempt,
        RoleManipulation,
        ExcessiveRepetition
    };
}
