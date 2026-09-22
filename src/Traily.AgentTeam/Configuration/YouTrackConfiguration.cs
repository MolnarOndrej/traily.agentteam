namespace Traily.AgentTeam.Configuration;

public sealed class YouTrackConfiguration
{
    public const string BaseUrlEnvironmentVariable =
        "TRAILY_YOUTRACK_BASE_URL";

    public const string TokenEnvironmentVariable =
        "TRAILY_YOUTRACK_TOKEN";

    private YouTrackConfiguration(
        Uri baseAddress,
        string accessToken)
    {
        BaseAddress = baseAddress;
        AccessToken = accessToken;
    }

    public Uri BaseAddress { get; }

    public string AccessToken { get; }

    public static YouTrackConfiguration FromEnvironment()
    {
        var baseUrl = Environment.GetEnvironmentVariable(
            BaseUrlEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException(
                $"Configure {BaseUrlEnvironmentVariable}.");
        }

        var normalizedBaseUrl =
            baseUrl.Trim().TrimEnd('/') + "/";

        if (!Uri.TryCreate(
                normalizedBaseUrl,
                UriKind.Absolute,
                out var baseAddress) ||
            baseAddress.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                $"{BaseUrlEnvironmentVariable} must be an HTTPS URL.");
        }

        var accessToken = Environment.GetEnvironmentVariable(
            TokenEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException(
                $"Configure {TokenEnvironmentVariable}.");
        }

        return new YouTrackConfiguration(
            baseAddress,
            accessToken.Trim());
    }
}