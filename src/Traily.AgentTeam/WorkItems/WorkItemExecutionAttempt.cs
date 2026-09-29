namespace Traily.AgentTeam.WorkItems;

public sealed class WorkItemExecutionAttempt
{
    public Guid Id { get; set; }

    public Guid WorkItemJobId { get; set; }

    // Set when the job is atomically claimed.
    public DateTimeOffset CreatedAt { get; set; }

    // Set when a provider actually begins executing.
    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    // Examples later: "codex-cli", "claude-cli".
    public string? ProviderId { get; set; }

    // Opaque to orchestration; interpreted by the chosen adapter.
    public string? ProviderSessionId { get; set; }

    // Filled before execution, using the latest full ticket.
    public string? TaskSnapshot { get; set; }

    // The specific workspace used by this attempt.
    public string? WorkingDirectory { get; set; }

    public WorkItemJob Job { get; set; } = null!;
}