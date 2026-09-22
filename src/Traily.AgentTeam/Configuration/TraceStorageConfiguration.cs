namespace Traily.AgentTeam.Configuration;

public static class TraceStorageConfiguration
{
    public const string DirectoryEnvironmentVariable =
        "TRAILY_TRACE_DIRECTORY";

    private const string ApplicationDirectoryName = "Traily";
    private const string TracesDirectoryName = "traces";

    public static string ResolveDirectory()
    {
        var configuredDirectory = Environment.GetEnvironmentVariable(
            DirectoryEnvironmentVariable);

        if (!string.IsNullOrWhiteSpace(configuredDirectory))
        {
            return Path.GetFullPath(configuredDirectory);
        }

        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new InvalidOperationException(
                $"Cannot determine the default trace directory. " +
                $"Configure {DirectoryEnvironmentVariable}.");
        }

        return Path.Combine(
            localAppData,
            ApplicationDirectoryName,
            TracesDirectoryName);
    }
}