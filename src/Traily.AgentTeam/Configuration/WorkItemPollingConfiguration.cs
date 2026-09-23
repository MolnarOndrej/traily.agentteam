namespace Traily.AgentTeam.Configuration;

public sealed record WorkItemPollingConfiguration(
    TimeSpan Interval)
{
    public const string IntervalEnvironmentVariable =
        "TRAILY_POLL_INTERVAL_SECONDS";

    public static WorkItemPollingConfiguration FromEnvironment()
    {
        var value = Environment.GetEnvironmentVariable(
            IntervalEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(value))
        {
            return new(TimeSpan.FromMinutes(1));
        }

        if (!int.TryParse(value, out var seconds) ||
            seconds < 10)
        {
            throw new InvalidOperationException(
                $"{IntervalEnvironmentVariable} must be " +
                "an integer of at least 10.");
        }

        return new(TimeSpan.FromSeconds(seconds));
    }
}