namespace AgentHub.API.Services.Skills;

/// <summary>
/// Base interface for all skills that can be applied to agent flows.
/// </summary>
/// <typeparam name="TInput">The input type for the skill</typeparam>
/// <typeparam name="TResult">The result type returned by the skill</typeparam>
public interface ISkill<TInput, TResult>
{
    /// <summary>
    /// Executes the skill with the provided input.
    /// </summary>
    /// <param name="input">The input data for the skill</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result of executing the skill</returns>
    Task<TResult> ExecuteAsync(TInput input, CancellationToken cancellationToken = default);
}
