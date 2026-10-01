namespace Traily.AgentTeam.Operations;

public enum OperationalAvailability
{
    Available,
    Unavailable,
    Unknown
}

public sealed record OperationalScope(
    string Type,
    string Id,
    string Capability);

public sealed record OperationalObservation(
    OperationalScope Scope,
    OperationalAvailability Availability,
    string? ReasonCode = null,
    string? Message = null,
    int? HttpStatusCode = null);

public enum OperationalIssueTransition
{
    None,
    Opened,
    Updated,
    Changed,
    Resolved
}