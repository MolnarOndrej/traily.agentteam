using Traily.AgentTeam.Operations;

namespace Traily.AgentTeam.Integrations.Http;

public enum HttpFailureKind
{
    AuthenticationFailed,
    AccessDenied,
    ResourceInaccessible,
    RateLimited,
    RemoteServiceFailure,
    RequestRejected,
    ConnectionFailed,
    RequestTimedOut
}

public sealed record HttpFailureClassification(
    HttpFailureKind Kind,
    string Message,
    int? HttpStatusCode = null)
{
    // The caller supplies context and capability interpretation, never raw response text.
    public OperationalObservation ToObservation(
        OperationalScope scope,
        OperationalAvailability availability,
        string context,
        string? reasonCode = null) =>
        new(scope, availability, reasonCode ?? Kind.ToString(),
            $"{context}: {Message}", HttpStatusCode);
}

public static class HttpFailureClassifier
{
    public static HttpFailureClassification Classify(int? statusCode)
    {
        if (statusCode is < 100 or > 599 or >= 200 and <= 299)
            throw new ArgumentOutOfRangeException(nameof(statusCode),
                "A failure classification requires a non-success HTTP status or no status.");

        var (kind, message) = statusCode switch
        {
            401 => (HttpFailureKind.AuthenticationFailed,
                "The service rejected the supplied authentication credentials."),
            403 => (HttpFailureKind.AccessDenied,
                "The service denied the request."),
            404 => (HttpFailureKind.ResourceInaccessible,
                "The requested resource is not accessible; this does not establish that it was deleted."),
            429 => (HttpFailureKind.RateLimited,
                "The service rate-limited the request."),
            >= 500 => (HttpFailureKind.RemoteServiceFailure,
                "The service returned a server error."),
            null => (HttpFailureKind.ConnectionFailed,
                "The request could not be completed without receiving an HTTP status."),
            _ => (HttpFailureKind.RequestRejected,
                "The service did not accept the request.")
        };
        return new HttpFailureClassification(kind, message, statusCode);
    }

    // Callers must propagate their own cancellation before classifying a timeout.
    public static HttpFailureClassification TimedOut() =>
        new(HttpFailureKind.RequestTimedOut,
            "The request timed out or was canceled independently of caller cancellation.");
}
