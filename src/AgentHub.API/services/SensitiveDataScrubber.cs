using System.Text.RegularExpressions;

namespace AgentHub.API.Services;

/// <summary>
/// Scrubs sensitive data patterns from text before it's stored in memory.
/// Prevents accidental exposure of PII, credentials, and financial information.
/// </summary>
public sealed class SensitiveDataScrubber
{
    private static readonly List<SensitivePattern> Patterns = new()
    {
        // Credit card patterns (Visa, Mastercard, Amex, Discover)
        new SensitivePattern(
            "CreditCard",
            new Regex(@"\b(?:\d{4}[-\s]?){3}\d{4}\b", RegexOptions.Compiled),
            "[REDACTED_CREDIT_CARD]"),

        // Social Security Number (XXX-XX-XXXX or XXXXXXXXX)
        new SensitivePattern(
            "SSN",
            new Regex(@"\b\d{3}-\d{2}-\d{4}\b|\b\d{9}\b", RegexOptions.Compiled),
            "[REDACTED_SSN]"),

        // Email addresses (optional, can be commented out if emails should be preserved)
        new SensitivePattern(
            "Email",
            new Regex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b", RegexOptions.Compiled),
            "[REDACTED_EMAIL]"),

        // API Keys (common patterns like "api_key=...", "Authorization: Bearer ...", "token=...", "key=...")
        new SensitivePattern(
            "APIKey",
            new Regex(@"(?:api_key|apikey|api-key|authorization|bearer|token|secret|key)[\s]*[:=][\s]*[^\s\r\n]+", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            "[REDACTED_API_KEY]"),

        // Phone numbers (XXX-XXX-XXXX or (XXX) XXX-XXXX)
        new SensitivePattern(
            "PhoneNumber",
            new Regex(@"\b(?:\d{3}[-.\s]?)?\d{3}[-.\s]?\d{4}\b", RegexOptions.Compiled),
            "[REDACTED_PHONE]"),

        // Common password patterns (password = "...", pwd: ..., "password is", etc.)
        new SensitivePattern(
            "Password",
            new Regex(@"(?:password|pwd|passwd)\s*(?:is|[:=])\s*[^\s\r\n]+", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            "[REDACTED_PASSWORD]"),

        // IP Addresses (IPv4)
        new SensitivePattern(
            "IPv4",
            new Regex(@"\b(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\b", RegexOptions.Compiled),
            "[REDACTED_IP]"),
    };

    /// <summary>
    /// Scrubs all known sensitive data patterns from the input text, reporting which types were found.
    /// </summary>
    public static ScrubResult Scrub(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new ScrubResult(text ?? string.Empty, []);

        var result = text;
        var detectedTypes = new List<string>();

        foreach (var pattern in Patterns)
        {
            if (pattern.Regex.IsMatch(result))
            {
                detectedTypes.Add(pattern.Name);
                result = pattern.Regex.Replace(result, pattern.Replacement);
            }
        }

        return new ScrubResult(result, detectedTypes);
    }

    /// <summary>
    /// Scrubs a message pair, returning scrubbed text and the union of detected sensitive types.
    /// </summary>
    public static ScrubPairResult ScrubMessagePair(string userMessage, string assistantResponse)
    {
        var userResult = Scrub(userMessage);
        var assistantResult = Scrub(assistantResponse);

        var allDetectedTypes = userResult.DetectedTypes
            .Union(assistantResult.DetectedTypes)
            .Distinct()
            .ToList();

        return new ScrubPairResult(
            userResult.ScrubbedText,
            assistantResult.ScrubbedText,
            allDetectedTypes);
    }

    /// <summary>
    /// Internal record for pattern matching
    /// </summary>
    private sealed record SensitivePattern(string Name, Regex Regex, string Replacement);
}

/// <summary>
/// Result of scrubbing a single piece of text.
/// </summary>
public sealed record ScrubResult(string ScrubbedText, IReadOnlyList<string> DetectedTypes)
{
    public bool HasSensitiveData => DetectedTypes.Count > 0;
}

/// <summary>
/// Result of scrubbing a user/assistant message pair.
/// </summary>
public sealed record ScrubPairResult(
    string ScrubbedUserMessage,
    string ScrubbedAssistantResponse,
    IReadOnlyList<string> DetectedTypes)
{
    public bool HasSensitiveData => DetectedTypes.Count > 0;
}
