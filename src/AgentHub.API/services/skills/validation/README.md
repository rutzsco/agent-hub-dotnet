# Prompt Validation Skill

## Overview

The Prompt Validation Skill provides comprehensive safety validation for user prompts before they are processed by AI agents. It detects and blocks common attack patterns including prompt injection, jailbreak attempts, role manipulation, and malicious input patterns.

## Features

### Security Validations

1. **Prompt Injection Detection**
   - Detects attempts to override system instructions
   - Patterns like "ignore previous instructions", "ignore all rules", "system: ignore"
   - Catches special tokens like `<|im_start|>`, `### Instruction`

2. **Jailbreak Attempt Detection**
   - Identifies attempts to bypass safety constraints
   - Patterns like "DAN mode", "do anything now", "developer mode", "bypass content filter"

3. **Role Manipulation Detection**
   - Catches attempts to alter agent behavior or identity
   - Patterns like "pretend to be", "act as if you have no restrictions", "forget you are"

4. **Input Quality Validations**
   - **Length Limits**: Enforces minimum (non-empty) and maximum (4000 characters) length
   - **Character Validation**: Blocks invalid control characters while allowing newlines, tabs, and carriage returns
   - **Excessive Repetition**: Detects and blocks patterns with excessive character repetition

## Architecture

```
services/
└── skills/
	├── ISkill.cs                          # Base interface for all skills
	└── validation/
		├── PromptValidationSkill.cs       # Main validation implementation
		├── PromptValidationResult.cs      # Result type with success/failure status
		└── ValidationRule.cs              # Validation rules library
```

## Usage

### Basic Usage

```csharp
// Injected via DI
public async Task ProcessMessage(PromptValidationSkill validationSkill)
{
	var input = new PromptValidationInput
	{
		Prompt = "User's message here",
		UserId = "user123"
	};

	var result = await validationSkill.ExecuteAsync(input);

	if (!result.IsValid)
	{
		// Validation failed
		Console.WriteLine($"Rule: {result.FailedRule}");
		Console.WriteLine($"Error: {result.ErrorMessage}");
		return;
	}

	// Proceed with safe prompt
	// ...
}
```

### Custom Validation Rules

You can create custom validation rules for specific requirements:

```csharp
var customRule = new ValidationRule
{
	Name = "NoNumbers",
	Validator = prompt =>
	{
		if (prompt.Any(char.IsDigit))
		{
			return (false, "Numbers are not allowed");
		}
		return (true, null);
	}
};

var skill = new PromptValidationSkill(
	new[] { customRule },
	logger);
```

## Integration

The skill is automatically integrated into the Foundry Memory Agent flow:

1. **Registration**: Added to DI container in `AgentHubServiceCollectionExtensions.AddSkills()`
2. **Integration**: Called in `FoundryMemoryAgent.ProcessMessage()` before agent execution
3. **Error Handling**: Validation failures throw `ArgumentException` with descriptive error messages

## Validation Rules

### Default Rules (Applied in Order)

1. **MinimumLength** - Ensures prompt is not empty
2. **MaximumLength** - Enforces 4000 character limit
3. **ValidCharacters** - Blocks invalid control characters
4. **PromptInjection** - Detects instruction override attempts
5. **JailbreakAttempt** - Catches safety bypass patterns
6. **RoleManipulation** - Identifies behavior alteration attempts
7. **ExcessiveRepetition** - Blocks malicious repetition patterns

### Customization

Rules can be customized by:
- Modifying regex patterns in `ValidationRule.cs`
- Creating new `ValidationRule` instances
- Passing custom rules to `PromptValidationSkill` constructor

## Testing

Comprehensive test coverage is provided in `PromptValidationSkillTests.cs`:

- ✅ Valid prompts pass validation
- ✅ Empty/whitespace prompts are rejected
- ✅ Prompts exceeding max length are rejected
- ✅ Prompt injection attempts are detected
- ✅ Jailbreak attempts are blocked
- ✅ Role manipulation is identified
- ✅ Invalid control characters are rejected
- ✅ Excessive repetition is detected
- ✅ Custom rules can be added

## Performance Considerations

- All regex patterns are pre-compiled for optimal performance
- Validation stops at the first failed rule (fail-fast)
- No external API calls - all validation is local
- Async-ready but executes synchronously (no I/O)

## Security Best Practices

1. **Defense in Depth**: This skill provides the first layer of defense. Azure AI also applies its own content filtering.
2. **Logging**: All validation failures are logged with context for security monitoring
3. **User Feedback**: Error messages are informative but don't reveal detection patterns
4. **Regular Updates**: Patterns should be updated as new attack vectors are discovered

## Future Enhancements

Potential improvements:
- Integration with Azure Content Safety API for advanced detection
- Machine learning-based pattern detection
- Rate limiting per user
- Configurable rule sets per agent/endpoint
- Prompt sanitization/normalization options

## Example Error Messages

```
Message cannot be empty.
Message exceeds maximum length of 4000 characters (current: 4523).
Invalid control characters detected in the message.
Potential prompt injection detected. The message contains patterns that attempt to override system instructions.
Potential jailbreak attempt detected. The message contains patterns that attempt to bypass safety constraints.
Role manipulation detected. The message attempts to alter the agent's behavior or identity.
Excessive repetition detected. The message contains repeated patterns that may indicate malicious input.
```
