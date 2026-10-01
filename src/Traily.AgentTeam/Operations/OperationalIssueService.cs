using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Traily.AgentTeam.Persistence;

namespace Traily.AgentTeam.Operations;

public sealed class OperationalIssueService(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<OperationalIssueService> logger)
{
    public async Task<OperationalIssueTransition> ObserveAsync(
        OperationalObservation observation,
        CancellationToken cancellationToken = default)
    {
        Validate(observation);

        // Issue persistence has its own context. A failed observation
        // write must not contaminate the caller's queue changes.
        await using var scope = scopeFactory.CreateAsyncScope();

        var database = scope.ServiceProvider
            .GetRequiredService<TrailyDbContext>();

        await using var transaction =
            await database.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        var issue = await database.OperationalIssues
            .SingleOrDefaultAsync(
                entry =>
                    entry.ScopeType == observation.Scope.Type &&
                    entry.ScopeId == observation.Scope.Id &&
                    entry.Capability == observation.Scope.Capability &&
                    entry.ResolvedAt == null,
                cancellationToken);

        var now = clock.GetUtcNow();
        OperationalIssueTransition transition;

        if (observation.Availability ==
            OperationalAvailability.Available)
        {
            if (issue is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return OperationalIssueTransition.None;
            }

            // Preserve the last failure details and observation time.
            issue.ResolvedAt = now;
            transition = OperationalIssueTransition.Resolved;
        }
        else if (issue is null)
        {
            issue = new OperationalIssue
            {
                Id = Guid.NewGuid(),
                ScopeType = observation.Scope.Type,
                ScopeId = observation.Scope.Id,
                Capability = observation.Scope.Capability,
                Availability = observation.Availability,
                ReasonCode = observation.ReasonCode!,
                Message = observation.Message!,
                HttpStatusCode = observation.HttpStatusCode,
                FirstObservedAt = now,
                LastObservedAt = now,
                ObservationCount = 1
            };

            database.OperationalIssues.Add(issue);
            transition = OperationalIssueTransition.Opened;
        }
        else
        {
            var changed =
                issue.Availability != observation.Availability ||
                issue.ReasonCode != observation.ReasonCode ||
                issue.Message != observation.Message ||
                issue.HttpStatusCode != observation.HttpStatusCode;

            issue.Availability = observation.Availability;
            issue.ReasonCode = observation.ReasonCode!;
            issue.Message = observation.Message!;
            issue.HttpStatusCode = observation.HttpStatusCode;
            issue.LastObservedAt = now;
            issue.ObservationCount++;

            transition = changed
                ? OperationalIssueTransition.Changed
                : OperationalIssueTransition.Updated;
        }

        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        LogOperationalTransition(issue, transition);

        return transition;
    }

    private void LogOperationalTransition(OperationalIssue issue, OperationalIssueTransition transition)
    {
        if (transition == OperationalIssueTransition.Resolved)
        {
            logger.LogInformation(
                "Operational issue {IssueId} resolved: " +
                "{ScopeType} {ScopeId}; capability {Capability}.",
                issue.Id,
                issue.ScopeType,
                issue.ScopeId,
                issue.Capability);
        }
        else if (transition is
            OperationalIssueTransition.Opened or
            OperationalIssueTransition.Changed)
        {
            logger.LogWarning(
                "Operational issue {IssueId} {Transition}: " +
                "{ScopeType} {ScopeId}; capability {Capability}; " +
                "{Availability}; {ReasonCode}; {Message}",
                issue.Id,
                transition,
                issue.ScopeType,
                issue.ScopeId,
                issue.Capability,
                issue.Availability,
                issue.ReasonCode,
                issue.Message);
        }
    }

    private static void Validate(
        OperationalObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(observation.Scope);

        RequireText(observation.Scope.Type, 100);
        RequireText(observation.Scope.Id, 200);
        RequireText(observation.Scope.Capability, 100);

        if (!Enum.IsDefined(observation.Availability))
        {
            throw new ArgumentOutOfRangeException(
                nameof(observation));
        }

        if (observation.HttpStatusCode is < 100 or > 599)
        {
            throw new ArgumentOutOfRangeException(
                nameof(observation));
        }

        if (observation.Availability ==
            OperationalAvailability.Available)
        {
            if (observation.ReasonCode is not null ||
                observation.Message is not null ||
                observation.HttpStatusCode is not null)
            {
                throw new ArgumentException(
                    "Available observations must not contain failure details.",
                    nameof(observation));
            }

            return;
        }

        RequireText(observation.ReasonCode, 100);
        RequireText(observation.Message, 1000);
    }

    private static void RequireText(
        string? value,
        int maximumLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (value.Length > maximumLength)
        {
            throw new ArgumentException(
                $"Observation text exceeds {maximumLength} characters.");
        }
    }
}