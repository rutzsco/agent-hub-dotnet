using AgentHub.API.Services;

namespace AgentHub.Tests;

/// <summary>
/// Tests for SensitiveDataScrubber to ensure PII and credentials are properly redacted
/// before being stored in memory.
/// </summary>
public class SensitiveDataScrubberTests
{
    [Theory]
    [InlineData("My credit card is 1234-5678-9012-3456", "[REDACTED_CREDIT_CARD]")]
    [InlineData("Card: 1111222233334445", "[REDACTED_CREDIT_CARD]")]
    [InlineData("Card: 1111-2222-3333-4445", "[REDACTED_CREDIT_CARD]")]
    public void Scrub_RedactsCreditCards(string input, string expectedFragment)
    {
        var result = SensitiveDataScrubber.Scrub(input);
        Assert.Contains(expectedFragment, result.ScrubbedText);
        Assert.Contains("CreditCard", result.DetectedTypes);
        Assert.True(result.HasSensitiveData);
    }

    [Theory]
    [InlineData("My SSN is 123-45-6789", "[REDACTED_SSN]")]
    [InlineData("SSN: 987654321", "[REDACTED_SSN]")]
    public void Scrub_RedactsSSN(string input, string expectedFragment)
    {
        var result = SensitiveDataScrubber.Scrub(input);
        Assert.Contains(expectedFragment, result.ScrubbedText);
        Assert.Contains("SSN", result.DetectedTypes);
    }

    [Theory]
    [InlineData("Contact me at john.doe@example.com", "[REDACTED_EMAIL]")]
    [InlineData("Email: alice+test@company.co.uk", "[REDACTED_EMAIL]")]
    public void Scrub_RedactsEmails(string input, string expectedFragment)
    {
        var result = SensitiveDataScrubber.Scrub(input);
        Assert.Contains(expectedFragment, result.ScrubbedText);
        Assert.Contains("Email", result.DetectedTypes);
    }

    [Theory]
    [InlineData("api_key=test-api-key-value", "[REDACTED_API_KEY]")]
    [InlineData("Authorization: test-bearer-token-value", "[REDACTED_API_KEY]")]
    [InlineData("token: test-token-value", "[REDACTED_API_KEY]")]
    public void Scrub_RedactsAPIKeys(string input, string expectedFragment)
    {
        var result = SensitiveDataScrubber.Scrub(input);
        Assert.Contains(expectedFragment, result.ScrubbedText);
        Assert.Contains("APIKey", result.DetectedTypes);
    }

    [Theory]
    [InlineData("Call me at 555-123-4567", "[REDACTED_PHONE]")]
    [InlineData("Phone: (555) 123-4567", "[REDACTED_PHONE]")]
    [InlineData("555.123.4567", "[REDACTED_PHONE]")]
    public void Scrub_RedactsPhoneNumbers(string input, string expectedFragment)
    {
        var result = SensitiveDataScrubber.Scrub(input);
        Assert.Contains(expectedFragment, result.ScrubbedText);
        Assert.Contains("PhoneNumber", result.DetectedTypes);
    }

    [Theory]
    [InlineData("password: MySecurePass123!", "[REDACTED_PASSWORD]")]
    [InlineData("pwd='super_secret'", "[REDACTED_PASSWORD]")]
    [InlineData("passwd=abc123xyz", "[REDACTED_PASSWORD]")]
    public void Scrub_RedactsPasswords(string input, string expectedFragment)
    {
        var result = SensitiveDataScrubber.Scrub(input);
        Assert.Contains(expectedFragment, result.ScrubbedText);
        Assert.Contains("Password", result.DetectedTypes);
    }

    [Theory]
    [InlineData("Server IP: 192.168.1.1", "[REDACTED_IP]")]
    [InlineData("Connect to 10.0.0.255", "[REDACTED_IP]")]
    public void Scrub_RedactsIPAddresses(string input, string expectedFragment)
    {
        var result = SensitiveDataScrubber.Scrub(input);
        Assert.Contains(expectedFragment, result.ScrubbedText);
        Assert.Contains("IPv4", result.DetectedTypes);
    }

    [Fact]
    public void Scrub_HandlesMultipleSensitivePatterns()
    {
        var input = "User: john.doe@example.com, SSN: 123-45-6789, Card: 1111222233334445, Phone: 555-123-4567";
        var result = SensitiveDataScrubber.Scrub(input);

        Assert.Contains("[REDACTED_EMAIL]", result.ScrubbedText);
        Assert.Contains("[REDACTED_SSN]", result.ScrubbedText);
        Assert.Contains("[REDACTED_CREDIT_CARD]", result.ScrubbedText);
        Assert.Contains("[REDACTED_PHONE]", result.ScrubbedText);

        // All four types should be reported
        Assert.Contains("Email", result.DetectedTypes);
        Assert.Contains("SSN", result.DetectedTypes);
        Assert.Contains("CreditCard", result.DetectedTypes);
        Assert.Contains("PhoneNumber", result.DetectedTypes);
        Assert.Equal(4, result.DetectedTypes.Count);

        // Original sensitive data should not be present
        Assert.DoesNotContain("john.doe@example.com", result.ScrubbedText);
        Assert.DoesNotContain("123-45-6789", result.ScrubbedText);
        Assert.DoesNotContain("1111222233334445", result.ScrubbedText);
        Assert.DoesNotContain("555-123-4567", result.ScrubbedText);
    }

    [Fact]
    public void Scrub_PreservesNonSensitiveData()
    {
        var input = "Hello, my name is John and I'm working on project X. The deadline is May 5, 2026.";
        var result = SensitiveDataScrubber.Scrub(input);

        // Regular text should be preserved
        Assert.Contains("Hello", result.ScrubbedText);
        Assert.Contains("John", result.ScrubbedText);
        Assert.Contains("project X", result.ScrubbedText);
        Assert.Contains("May 5, 2026", result.ScrubbedText);

        // No sensitive data reported
        Assert.False(result.HasSensitiveData);
        Assert.Empty(result.DetectedTypes);
    }

    [Fact]
    public void Scrub_HandleNullInput()
    {
        var result = SensitiveDataScrubber.Scrub(null);
        Assert.Equal(string.Empty, result.ScrubbedText);
        Assert.False(result.HasSensitiveData);
    }

    [Fact]
    public void Scrub_HandlesEmptyInput()
    {
        var result = SensitiveDataScrubber.Scrub(string.Empty);
        Assert.Equal(string.Empty, result.ScrubbedText);
        Assert.False(result.HasSensitiveData);
    }

    [Fact]
    public void ScrubMessagePair_ScrubsBothMessages()
    {
        var userMsg = "My email is alice@company.com and my phone is 555-123-4567";
        var assistantMsg = "Got it. Your credit card is 1111222233334445.";

        var result = SensitiveDataScrubber.ScrubMessagePair(userMsg, assistantMsg);

        Assert.Contains("[REDACTED_EMAIL]", result.ScrubbedUserMessage);
        Assert.Contains("[REDACTED_PHONE]", result.ScrubbedUserMessage);
        Assert.DoesNotContain("@company.com", result.ScrubbedUserMessage);

        Assert.Contains("[REDACTED_CREDIT_CARD]", result.ScrubbedAssistantResponse);
        Assert.DoesNotContain("1111222233334445", result.ScrubbedAssistantResponse);

        // Union of both messages' types
        Assert.Contains("Email", result.DetectedTypes);
        Assert.Contains("PhoneNumber", result.DetectedTypes);
        Assert.Contains("CreditCard", result.DetectedTypes);
        Assert.True(result.HasSensitiveData);
    }

    [Fact]
    public void Scrub_RealWorldSecurityConversation()
    {
        var sensitiveConversation = @"
User: I need to reset my password. My current password is SuperSecret123!
Assistant: Sure, I can help. To verify, what's the last 4 digits of your SSN?
User: It's 123-45-6789, and my credit card is 1111222233334445. Call me at (555) 123-4567.
Assistant: Got it. Here's your reset link, and don't share your API key: test-api-key-value
";

        var result = SensitiveDataScrubber.Scrub(sensitiveConversation);

        // All sensitive data should be redacted
        Assert.DoesNotContain("SuperSecret123", result.ScrubbedText);
        Assert.DoesNotContain("123-45-6789", result.ScrubbedText);
        Assert.DoesNotContain("1111222233334445", result.ScrubbedText);
        Assert.DoesNotContain("555-123-4567", result.ScrubbedText);
        Assert.DoesNotContain("test-api-key-value", result.ScrubbedText);

        // Redaction markers should be present
        Assert.Contains("[REDACTED_PASSWORD]", result.ScrubbedText);
        Assert.Contains("[REDACTED_SSN]", result.ScrubbedText);
        Assert.Contains("[REDACTED_CREDIT_CARD]", result.ScrubbedText);
        Assert.Contains("[REDACTED_PHONE]", result.ScrubbedText);
        Assert.Contains("[REDACTED_API_KEY]", result.ScrubbedText);

        // All types should be reported
        Assert.Contains("Password", result.DetectedTypes);
        Assert.Contains("SSN", result.DetectedTypes);
        Assert.Contains("CreditCard", result.DetectedTypes);
        Assert.Contains("PhoneNumber", result.DetectedTypes);
        Assert.Contains("APIKey", result.DetectedTypes);
        Assert.True(result.HasSensitiveData);
    }
}
