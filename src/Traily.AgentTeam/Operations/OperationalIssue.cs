namespace Traily.AgentTeam.Operations;

public sealed class OperationalIssue
{
    public Guid Id { get; set; }

    public required string ScopeType { get; set; }

    public required string ScopeId { get; set; }

    public required string Capability { get; set; }

    public OperationalAvailability Availability { get; set; }

    public required string ReasonCode { get; set; }

    public required string Message { get; set; }

    public int? HttpStatusCode { get; set; }

    public DateTimeOffset FirstObservedAt { get; set; }

    public DateTimeOffset LastObservedAt { get; set; }

    public long ObservationCount { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }
}