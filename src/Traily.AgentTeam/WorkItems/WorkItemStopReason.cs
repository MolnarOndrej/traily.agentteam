namespace Traily.AgentTeam.WorkItems;

public enum WorkItemStopReason
{
    TicketStateChanged,
    TicketAssignmentChanged,
    TicketIdentityChanged,
    TicketReadFailed,
    ProviderFailed,
    UsageLimitReached
}