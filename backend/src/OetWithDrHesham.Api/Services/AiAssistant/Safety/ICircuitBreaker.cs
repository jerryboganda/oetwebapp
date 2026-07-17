namespace OetWithDrHesham.Api.Services.AiAssistant.Safety;

/// <summary>
/// Circuit breaker for tool execution failures. Prevents cascading failures
/// by temporarily blocking tool invocation after repeated failures.
/// </summary>
public interface ICircuitBreaker
{
    /// <summary>Returns true if circuit is open (tool should NOT be called).</summary>
    Task<bool> IsOpenAsync(string toolCode, CancellationToken ct);

    /// <summary>Record a successful execution, potentially closing the circuit.</summary>
    Task RecordSuccessAsync(string toolCode, CancellationToken ct);

    /// <summary>Record a failed execution, potentially opening the circuit.</summary>
    Task RecordFailureAsync(string toolCode, CancellationToken ct);
}
